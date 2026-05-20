using System.Data.Common;
using LibraryMPT.Data;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksApiController : ControllerBase
{
    private readonly LibraryContext _context;

    public BooksApiController(LibraryContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<Book>>> GetAll()
    {
        var books = await _context.Books
            .FromSqlRaw("""
                SELECT
                    bookid AS "BookID",
                    title AS "Title",
                    description AS "Description",
                    publishyear AS "PublishYear",
                    authorid AS "AuthorID",
                    publisherid AS "PublisherID",
                    filepath AS "FilePath",
                    imagepath AS "ImagePath",
                    requiressubscription AS "RequiresSubscription"
                FROM books
                ORDER BY bookid
            """)
            .AsNoTracking()
            .ToListAsync();

        if (books.Count > 0)
        {
            var bookIds = books.Select(b => b.BookID).ToList();
            var authorIds = books.Where(b => b.AuthorID > 0).Select(b => b.AuthorID).Distinct().ToList();
            var bcRows = await _context.Database
                .SqlQuery<BookCategoryRow>($"""
                    SELECT bookid AS "BookID", categoryid AS "CategoryID"
                    FROM bookcategories
                    WHERE bookid = ANY({bookIds.ToArray()})
                    """)
                .ToListAsync();
            var catIds = bcRows.Select(r => r.CategoryID).Distinct().ToList();

            var authors = authorIds.Count > 0
                ? await _context.Authors
                    .FromSqlRaw($"""
                        SELECT authorid AS "AuthorID", firstname AS "FirstName", lastname AS "LastName"
                        FROM authors
                        WHERE authorid IN ({string.Join(",", authorIds)})
                        """)
                    .AsNoTracking()
                    .ToListAsync()
                : new List<Author>();
            var categories = catIds.Count > 0
                ? await _context.Categories
                    .FromSqlRaw($"""
                        SELECT categoryid AS "CategoryID", categoryname AS "CategoryName"
                        FROM categories
                        WHERE categoryid IN ({string.Join(",", catIds)})
                        """)
                    .AsNoTracking()
                    .ToListAsync()
                : new List<Category>();

            foreach (var b in books)
            {
                b.Author = authors.FirstOrDefault(a => a.AuthorID == b.AuthorID);
                b.CategoryIds = bcRows.Where(r => r.BookID == b.BookID).Select(r => r.CategoryID).ToList();
                b.Categories = categories.Where(c => b.CategoryIds.Contains(c.CategoryID)).ToList();
            }
        }

        return Ok(books);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Book>> GetById(int id)
    {
        var book = await _context.Books
            .FromSqlRaw("""
                SELECT
                    bookid AS "BookID",
                    title AS "Title",
                    description AS "Description",
                    publishyear AS "PublishYear",
                    authorid AS "AuthorID",
                    publisherid AS "PublisherID",
                    filepath AS "FilePath",
                    imagepath AS "ImagePath",
                    requiressubscription AS "RequiresSubscription"
                FROM books
                WHERE bookid = @id
            """, new NpgsqlParameter("@id", id))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (book == null)
            return NotFound();

        if (book.AuthorID > 0)
        {
            book.Author = await _context.Authors
                .FromSqlRaw("""
                    SELECT authorid AS "AuthorID", firstname AS "FirstName", lastname AS "LastName"
                    FROM authors
                    WHERE authorid = @id
                    """, new NpgsqlParameter("@id", book.AuthorID))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        if (book.PublisherID.HasValue)
        {
            book.Publisher = await _context.Publisher
                .FromSqlRaw("""
                    SELECT publisherid AS "PublisherID", publishername AS "PublisherName"
                    FROM publisher
                    WHERE publisherid = @id
                    """, new NpgsqlParameter("@id", book.PublisherID.Value))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        var bcRows = await _context.Database
            .SqlQuery<BookCategoryRow>($"""
                SELECT bookid AS "BookID", categoryid AS "CategoryID"
                FROM bookcategories
                WHERE bookid = {id}
                """)
            .ToListAsync();
        book.CategoryIds = bcRows.Select(r => r.CategoryID).ToList();
        if (book.CategoryIds.Count > 0)
        {
            var catIdsParam = new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            {
                Value = book.CategoryIds.ToArray()
            };
            book.Categories = await _context.Categories
                .FromSqlRaw("""
                    SELECT categoryid AS "CategoryID", categoryname AS "CategoryName"
                    FROM categories
                    WHERE categoryid = ANY(@ids)
                    """, catIdsParam)
                .AsNoTracking()
                .ToListAsync();
        }

        return Ok(book);
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> GetLookups()
    {
        var authors = await _context.Authors
            .FromSqlRaw("SELECT authorid AS \"AuthorID\", firstname AS \"FirstName\", lastname AS \"LastName\" FROM authors")
            .AsNoTracking()
            .ToListAsync();
        var categories = await _context.Categories
            .FromSqlRaw("SELECT categoryid AS \"CategoryID\", categoryname AS \"CategoryName\" FROM categories")
            .AsNoTracking()
            .ToListAsync();
        var publishers = await _context.Publisher
            .FromSqlRaw("SELECT publisherid AS \"PublisherID\", publishername AS \"PublisherName\" FROM publisher")
            .AsNoTracking()
            .ToListAsync();

        return Ok(new
        {
            authors,
            categories,
            publishers
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Book book)
    {
        var ids = book.CategoryIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
            return BadRequest(new { message = "Выберите хотя бы один жанр." });

        var conn = _context.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO books (title, description, publishyear, authorid, publisherid, filepath, imagepath)
            VALUES (@Title, @Description, @PublishYear, @AuthorID, @PublisherID, @FilePath, @ImagePath)
            RETURNING bookid
            """;
        cmd.Parameters.Add(new NpgsqlParameter("@Title", book.Title ?? ""));
        cmd.Parameters.Add(new NpgsqlParameter("@Description", (object?)book.Description ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("@PublishYear", (object?)book.PublishYear ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("@AuthorID", book.AuthorID));
        cmd.Parameters.Add(new NpgsqlParameter("@PublisherID", (object?)book.PublisherID ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("@FilePath", (object?)book.FilePath ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("@ImagePath", (object?)book.ImagePath ?? DBNull.Value));
        var newBookId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        foreach (var catId in ids)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO bookcategories (bookid, categoryid) VALUES (@bookId, @categoryId)",
                new NpgsqlParameter("@bookId", newBookId),
                new NpgsqlParameter("@categoryId", catId));
        }

        return Ok();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] Book book)
    {
        if (id != book.BookID)
            return BadRequest();

        var ids = book.CategoryIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
            return BadRequest(new { message = "Выберите хотя бы один жанр." });

        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE books SET
                title = @Title,
                description = @Description,
                publishyear = @PublishYear,
                authorid = @AuthorID,
                publisherid = @PublisherID,
                filepath = @FilePath,
                imagepath = @ImagePath
            WHERE bookid = @BookID
            """,
            new NpgsqlParameter("@Title", book.Title ?? ""),
            new NpgsqlParameter("@Description", (object?)book.Description ?? DBNull.Value),
            new NpgsqlParameter("@PublishYear", (object?)book.PublishYear ?? DBNull.Value),
            new NpgsqlParameter("@AuthorID", book.AuthorID),
            new NpgsqlParameter("@PublisherID", (object?)book.PublisherID ?? DBNull.Value),
            new NpgsqlParameter("@FilePath", (object?)book.FilePath ?? DBNull.Value),
            new NpgsqlParameter("@ImagePath", (object?)book.ImagePath ?? DBNull.Value),
            new NpgsqlParameter("@BookID", book.BookID));

        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM bookcategories WHERE bookid = @id",
            new NpgsqlParameter("@id", book.BookID));
        foreach (var catId in ids)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO bookcategories (bookid, categoryid) VALUES (@bookId, @categoryId)",
                new NpgsqlParameter("@bookId", book.BookID),
                new NpgsqlParameter("@categoryId", catId));
        }

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM books WHERE bookid = @id",
            new NpgsqlParameter("@id", id)
        );

        return Ok();
    }
}

