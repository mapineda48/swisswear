using Microsoft.EntityFrameworkCore;
using Moq;
using SwissWear.Web.Data;
using SwissWear.Web.Services;

namespace SwissWear.Tests;

public class PersonServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IStorageService> _storageMock;
    private readonly PersonService _service;

    public PersonServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _storageMock = new Mock<IStorageService>();
        _service = new PersonService(_db, _storageMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private static Person CreatePerson(string firstName = "John", string lastName = "Doe", string? email = null)
    {
        return new Person
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Phone = "+1234567890",
            BirthDate = new DateOnly(1990, 5, 15)
        };
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoPeople()
    {
        var result = await _service.GetAllAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPeopleOrderedByLastNameThenFirstName()
    {
        _db.People.AddRange(
            CreatePerson("Carlos", "Zapata"),
            CreatePerson("Ana", "Garcia"),
            CreatePerson("Zoe", "Garcia")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetAllAsync();

        Assert.Equal(3, result.Count);
        Assert.Equal("Ana", result[0].FirstName);
        Assert.Equal("Zoe", result[1].FirstName);
        Assert.Equal("Carlos", result[2].FirstName);
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_ReturnsPerson_WhenExists()
    {
        var person = CreatePerson();
        _db.People.Add(person);
        await _db.SaveChangesAsync();

        var result = await _service.GetByIdAsync(person.Id);

        Assert.NotNull(result);
        Assert.Equal("John", result.FirstName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(999);

        Assert.Null(result);
    }

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsync_SavesPersonToDatabase()
    {
        var person = CreatePerson("Jane", "Smith", "jane@test.com");

        var result = await _service.CreateAsync(person);

        Assert.True(result.Id > 0);
        Assert.Equal("Jane", result.FirstName);
        Assert.Single(_db.People);
    }

    [Fact]
    public async Task CreateAsync_WithPhoto_UploadsAndSetsPhotoUrl()
    {
        var person = CreatePerson();
        using var stream = new MemoryStream([0x01, 0x02, 0x03]);
        var expectedUrl = "http://storage/uploads/people/photo.jpg";

        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", default))
            .ReturnsAsync(expectedUrl);

        var result = await _service.CreateAsync(person, stream, "photo.jpg", "image/jpeg");

        Assert.Equal(expectedUrl, result.PhotoUrl);
        _storageMock.Verify(s => s.UploadAsync(
            It.Is<string>(n => n.StartsWith("people/") && n.EndsWith(".jpg")),
            stream,
            "image/jpeg",
            default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithoutPhoto_DoesNotUpload()
    {
        var person = CreatePerson();

        await _service.CreateAsync(person);

        Assert.Null(person.PhotoUrl);
        _storageMock.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DefaultsContentTypeToJpeg_WhenNull()
    {
        var person = CreatePerson();
        using var stream = new MemoryStream([0x01]);

        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", default))
            .ReturnsAsync("http://storage/photo.jpg");

        await _service.CreateAsync(person, stream, "photo.jpg", null);

        _storageMock.Verify(s => s.UploadAsync(It.IsAny<string>(), stream, "image/jpeg", default), Times.Once);
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsync_UpdatesPersonFields()
    {
        var person = CreatePerson();
        _db.People.Add(person);
        await _db.SaveChangesAsync();
        _db.Entry(person).State = EntityState.Detached;

        person.FirstName = "Updated";
        person.Email = "updated@test.com";
        await _service.UpdateAsync(person);

        var updated = await _db.People.FindAsync(person.Id);
        Assert.Equal("Updated", updated!.FirstName);
        Assert.Equal("updated@test.com", updated.Email);
    }

    [Fact]
    public async Task UpdateAsync_WithNewPhoto_UploadsAndDeletesOldPhoto()
    {
        var person = CreatePerson();
        person.PhotoUrl = "http://storage/devstoreaccount1/uploads/people/old-guid.jpg";
        _db.People.Add(person);
        await _db.SaveChangesAsync();
        _db.Entry(person).State = EntityState.Detached;

        using var stream = new MemoryStream([0x01]);
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/png", default))
            .ReturnsAsync("http://storage/new-photo.png");
        _storageMock
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        await _service.UpdateAsync(person, stream, "new.png", "image/png");

        Assert.Equal("http://storage/new-photo.png", person.PhotoUrl);
        _storageMock.Verify(s => s.DeleteAsync("devstoreaccount1/uploads/people/old-guid.jpg", default), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNewPhoto_NoOldPhoto_DoesNotDelete()
    {
        var person = CreatePerson();
        _db.People.Add(person);
        await _db.SaveChangesAsync();
        _db.Entry(person).State = EntityState.Detached;

        using var stream = new MemoryStream([0x01]);
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", default))
            .ReturnsAsync("http://storage/photo.jpg");

        await _service.UpdateAsync(person, stream, "photo.jpg", "image/jpeg");

        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>(), default), Times.Never);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_RemovesPersonFromDatabase()
    {
        var person = CreatePerson();
        _db.People.Add(person);
        await _db.SaveChangesAsync();

        await _service.DeleteAsync(person.Id);

        Assert.Empty(_db.People);
    }

    [Fact]
    public async Task DeleteAsync_WithPhoto_DeletesPhotoFromStorage()
    {
        var person = CreatePerson();
        person.PhotoUrl = "http://storage/devstoreaccount1/uploads/people/some-guid.jpg";
        _db.People.Add(person);
        await _db.SaveChangesAsync();

        _storageMock.Setup(s => s.DeleteAsync(It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        await _service.DeleteAsync(person.Id);

        Assert.Empty(_db.People);
        _storageMock.Verify(s => s.DeleteAsync("devstoreaccount1/uploads/people/some-guid.jpg", default), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithoutPhoto_DoesNotDeleteFromStorage()
    {
        var person = CreatePerson();
        _db.People.Add(person);
        await _db.SaveChangesAsync();

        await _service.DeleteAsync(person.Id);

        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_DoesNothing()
    {
        var person = CreatePerson();
        _db.People.Add(person);
        await _db.SaveChangesAsync();

        await _service.DeleteAsync(999);

        Assert.Single(_db.People);
    }

    [Fact]
    public async Task DeleteAsync_StorageDeleteFails_StillRemovesPerson()
    {
        var person = CreatePerson();
        person.PhotoUrl = "http://storage/devstoreaccount1/uploads/people/fail.jpg";
        _db.People.Add(person);
        await _db.SaveChangesAsync();

        _storageMock
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new Exception("Storage unavailable"));

        await _service.DeleteAsync(person.Id);

        Assert.Empty(_db.People);
    }
}
