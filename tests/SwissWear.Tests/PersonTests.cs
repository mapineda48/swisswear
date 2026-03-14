using System.ComponentModel.DataAnnotations;
using SwissWear.Web.Data;

namespace SwissWear.Tests;

public class PersonTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Person_ValidModel_PassesValidation()
    {
        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            Phone = "+1234567890",
            BirthDate = new DateOnly(1990, 1, 1)
        };

        var results = ValidateModel(person);

        Assert.Empty(results);
    }

    [Fact]
    public void Person_MissingFirstName_FailsValidation()
    {
        var person = new Person { FirstName = "", LastName = "Doe" };

        var results = ValidateModel(person);

        Assert.Contains(results, r => r.MemberNames.Contains("FirstName"));
    }

    [Fact]
    public void Person_MissingLastName_FailsValidation()
    {
        var person = new Person { FirstName = "John", LastName = "" };

        var results = ValidateModel(person);

        Assert.Contains(results, r => r.MemberNames.Contains("LastName"));
    }

    [Fact]
    public void Person_InvalidEmail_FailsValidation()
    {
        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email"
        };

        var results = ValidateModel(person);

        Assert.Contains(results, r => r.MemberNames.Contains("Email"));
    }

    [Fact]
    public void Person_NullOptionalFields_PassesValidation()
    {
        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            Email = null,
            Phone = null,
            BirthDate = null,
            PhotoUrl = null
        };

        var results = ValidateModel(person);

        Assert.Empty(results);
    }

    [Fact]
    public void Person_FirstNameExceedsMaxLength_FailsValidation()
    {
        var person = new Person
        {
            FirstName = new string('A', 101),
            LastName = "Doe"
        };

        var results = ValidateModel(person);

        Assert.Contains(results, r => r.MemberNames.Contains("FirstName"));
    }

    [Fact]
    public void Person_CreatedAt_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var person = new Person { FirstName = "John", LastName = "Doe" };
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(person.CreatedAt, before, after);
    }
}
