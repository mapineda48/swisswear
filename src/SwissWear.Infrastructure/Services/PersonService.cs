using Microsoft.EntityFrameworkCore;
using SwissWear.Domain.Contracts;
using SwissWear.Domain.Entities;
using SwissWear.Infrastructure.Data;

namespace SwissWear.Infrastructure.Services;

public class PersonService
{
    private readonly AppDbContext _db;
    private readonly IStorageService _storage;

    public PersonService(AppDbContext db, IStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<List<Person>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.People.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync(ct);
    }

    public async Task<Person?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.People.FindAsync([id], ct);
    }

    public async Task<Person> CreateAsync(Person person, Stream? photoStream = null, string? photoFileName = null, string? photoContentType = null, CancellationToken ct = default)
    {
        if (photoStream is not null && photoFileName is not null)
        {
            var storedName = $"people/{Guid.NewGuid()}{Path.GetExtension(photoFileName)}";
            person.PhotoUrl = await _storage.UploadAsync(storedName, photoStream, photoContentType ?? "image/jpeg", ct);
        }

        _db.People.Add(person);
        await _db.SaveChangesAsync(ct);
        return person;
    }

    public async Task UpdateAsync(Person person, Stream? photoStream = null, string? photoFileName = null, string? photoContentType = null, CancellationToken ct = default)
    {
        if (photoStream is not null && photoFileName is not null)
        {
            if (!string.IsNullOrEmpty(person.PhotoUrl))
            {
                await TryDeleteOldPhoto(person.PhotoUrl, ct);
            }

            var storedName = $"people/{Guid.NewGuid()}{Path.GetExtension(photoFileName)}";
            person.PhotoUrl = await _storage.UploadAsync(storedName, photoStream, photoContentType ?? "image/jpeg", ct);
        }

        _db.People.Update(person);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var person = await _db.People.FindAsync([id], ct);
        if (person is null) return;

        if (!string.IsNullOrEmpty(person.PhotoUrl))
        {
            await TryDeleteOldPhoto(person.PhotoUrl, ct);
        }

        _db.People.Remove(person);
        await _db.SaveChangesAsync(ct);
    }

    private async Task TryDeleteOldPhoto(string photoUrl, CancellationToken ct)
    {
        try
        {
            var uri = new Uri(photoUrl);
            var blobName = uri.AbsolutePath.TrimStart('/');
            var containerPrefix = "uploads/";
            if (blobName.StartsWith(containerPrefix))
                blobName = blobName[containerPrefix.Length..];
            await _storage.DeleteAsync(blobName, ct);
        }
        catch
        {
            // Best effort — don't fail the operation if old photo can't be deleted
        }
    }
}
