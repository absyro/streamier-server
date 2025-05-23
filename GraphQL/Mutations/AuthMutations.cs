namespace StreamierGraphQLServer.GraphQL.Mutations;

using Microsoft.EntityFrameworkCore;
using OtpNet;
using RandomString4Net;
using StreamierGraphQLServer.Contexts;
using StreamierGraphQLServer.GraphQL.Context;
using StreamierGraphQLServer.GraphQL.Utils;
using StreamierGraphQLServer.Inputs.Auth;
using StreamierGraphQLServer.Models;
using Zxcvbn;

/// <summary>
/// Contains mutations related to user authentication including signup, signin, and session management.
/// </summary>
[ExtendObjectType("Mutation")]
public class AuthMutations
{
    /// <summary>
    /// Creates a new user account with the provided credentials and information.
    /// </summary>
    public async Task<User> SignUp([Service] AppDbContext dbContext, SignUpInput input)
    {
        ValidationUtils.ValidateInput(input);

        if (await dbContext.Users.AnyAsync(u => u.Email == input.Email))
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Email already exists")
                    .SetCode("EMAIL_ALREADY_EXISTS")
                    .SetExtension("email", input.Email)
                    .Build()
            );
        }

        var result = Core.EvaluatePassword(input.Password);

        if (result.Score < 3)
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Password is too weak")
                    .SetCode("WEAK_PASSWORD")
                    .SetExtension("feedback", result.Feedback)
                    .Build()
            );
        }

        string id;

        do
        {
            id = RandomString.GetString(Types.ALPHANUMERIC_LOWERCASE, 8);
        } while (await dbContext.Users.AnyAsync(u => u.Id == id));

        var user = new User
        {
            Id = id,
            Email = input.Email,
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(
                input.Password,
                BCrypt.Net.BCrypt.GenerateSalt(12)
            ),
            PrivacySettings = new UserPrivacySettings() { Id = id },
            Preferences = new UserPreferences() { Id = id },
        };

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        return user;
    }

    /// <summary>
    /// Creates a new authentication session for an existing user.
    /// </summary>
    public async Task<UserSession> SignIn([Service] AppDbContext dbContext, SignInInput input)
    {
        ValidationUtils.ValidateInput(input);

        var now = DateTime.UtcNow;

        var minExpiration = now.AddHours(1);
        var maxExpiration = now.AddDays(365);

        if (input.ExpirationDate < minExpiration || input.ExpirationDate > maxExpiration)
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Invalid expiration date")
                    .SetCode("INVALID_EXPIRATION_DATE")
                    .Build()
            );
        }

        var user =
            await dbContext
                .Users.Include(u => u.Sessions)
                .FirstOrDefaultAsync(u => u.Email == input.Email)
            ?? throw new GraphQLException(
                ErrorBuilder.New().SetMessage("User not found").SetCode("USER_NOT_FOUND").Build()
            );

        if (!BCrypt.Net.BCrypt.Verify(input.Password, user.HashedPassword))
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Invalid password")
                    .SetCode("INVALID_PASSWORD")
                    .Build()
            );
        }

        if (user.TwoFactorAuthentication != null)
        {
            if (input.TwoFactorAuthenticationCode == null)
            {
                throw new GraphQLException(
                    ErrorBuilder
                        .New()
                        .SetMessage("Two-factor authentication code required")
                        .SetCode("2FA_REQUIRED")
                        .Build()
                );
            }

            var totp = new Totp(Base32Encoding.ToBytes(user.TwoFactorAuthentication.Secret));

            if (
                !totp.VerifyTotp(
                    input.TwoFactorAuthenticationCode,
                    out _,
                    new VerificationWindow(1, 1)
                )
            )
            {
                if (
                    !user.TwoFactorAuthentication.RecoveryCodes.Contains(
                        input.TwoFactorAuthenticationCode
                    )
                )
                {
                    throw new GraphQLException(
                        ErrorBuilder
                            .New()
                            .SetMessage("Invalid two-factor authentication code")
                            .SetCode("INVALID_2FA_CODE")
                            .Build()
                    );
                }

                user.TwoFactorAuthentication.RecoveryCodes.Remove(
                    input.TwoFactorAuthenticationCode
                );

                await dbContext.SaveChangesAsync();
            }
        }

        const int MaxSessionsPerUser = 5;

        if (user.Sessions.Count >= MaxSessionsPerUser)
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        $"Maximum number of sessions per user ({MaxSessionsPerUser}) reached"
                    )
                    .SetCode("MAX_SESSIONS_PER_USER")
                    .Build()
            );
        }

        string id;

        do
        {
            id = RandomString.GetString(Types.ALPHANUMERIC_MIXEDCASE_WITH_SYMBOLS, 128);
        } while (await dbContext.Users.AnyAsync(u => u.Sessions.Any(s => s.Id == id)));

        var session = new UserSession { Id = id, ExpiresAt = input.ExpirationDate };

        user.Sessions.Add(session);

        await dbContext.SaveChangesAsync();

        return session;
    }

    /// <summary>
    /// Deletes the current user session, effectively logging the user out.
    /// </summary>
    public async Task<bool> DeleteSession(
        [Service] AppDbContext dbContext,
        [Service] GraphQLContext graphQLContext
    )
    {
        var sessionId = graphQLContext.GetSessionId();

        if (string.IsNullOrEmpty(sessionId))
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Session ID is required")
                    .SetCode("SESSION_REQUIRED")
                    .Build()
            );
        }

        var user = await dbContext
            .Users.Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Sessions.Any(s => s.Id == sessionId));

        if (user == null)
        {
            return false;
        }

        var session = user.Sessions.FirstOrDefault(s => s.Id == sessionId);

        if (session == null)
        {
            return false;
        }

        user.Sessions.Remove(session);

        await dbContext.SaveChangesAsync();

        return true;
    }
}
