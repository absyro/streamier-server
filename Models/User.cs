namespace Server.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Represents a user in the system.
/// </summary>
public class User : Base.BaseEntity
{
    /// <summary>
    /// The user's email address.
    /// </summary>
    [Required]
    [EmailAddress]
    [Column("email")]
    public required string Email { get; set; }

    /// <summary>
    /// Whether the user's email address has been verified.
    /// </summary>
    [Required]
    [Column("is_email_verified")]
    public required bool IsEmailVerified { get; set; }

    /// <summary>
    /// The user's hashed password.
    /// </summary>
    [GraphQLIgnore]
    [Required]
    [Column("hashed_password")]
    public required string HashedPassword { get; set; }

    /// <summary>
    /// Hashes a password using BCrypt.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <returns>The hashed password.</returns>
    public static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
    }

    /// <summary>
    /// Validates a password against a hashed password using BCrypt.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <param name="hashedPassword">The hashed password to validate against.</param>
    /// <returns>True if the password is valid, false otherwise.</returns>
    public static bool ValidatePassword(string password, string hashedPassword)
    {
        return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }
}
