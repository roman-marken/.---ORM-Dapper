using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

const string Cs =
    @"Server=(localdb)\mssqllocaldb;Database=MailingListPromoUkDb;Integrated Security=true;TrustServerCertificate=true;";

SqlConnection? db = null;

await RunAsync();

async Task RunAsync()
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("=== Список розсилки (Dapper) ===");
        var stan = IsOpen() && db != null ? $"з’єднано з «{db.Database}»" : "немає з’єднання";
        Console.WriteLine($"Стан: {stan}");
        Console.WriteLine("1 — Підключитися до бази даних");
        Console.WriteLine("2 — Від’єднатися від бази даних");
        Console.WriteLine("3 — Усі покупці");
        Console.WriteLine("4 — Електронні пошти усіх покупців");
        Console.WriteLine("5 — Перелік розділів");
        Console.WriteLine("6 — Перелік акційних товарів (акційні пропозиції)");
        Console.WriteLine("7 — Усі міста");
        Console.WriteLine("8 — Усі країни");
        Console.WriteLine("9 — Покупці за містом");
        Console.WriteLine("10 — Покупці за країною");
        Console.WriteLine("11 — Усі акційні пропозиції для певної країни");
        Console.WriteLine("0 — Вихід");
        Console.Write("Вибір: ");
        var k = Console.ReadLine()?.Trim();
        if (k == "0") break;
        switch (k)
        {
            case "1":
                await ConnectAsync(); break;
            case "2":
                Disconnect(); break;
            case "3":
                Require(() => ShowBuyers(db!)); break;
            case "4":
                Require(() => ShowEmails(db!)); break;
            case "5":
                Require(() => ShowSections(db!)); break;
            case "6":
                Require(() => ShowPromos(db!)); break;
            case "7":
                Require(() => ShowCities(db!)); break;
            case "8":
                Require(() => ShowCountries(db!)); break;
            case "9":
                Require(() => ShowBuyersByCity(db!)); break;
            case "10":
                Require(() => ShowBuyersByCountry(db!)); break;
            case "11":
                Require(() => ShowPromosByCountry(db!)); break;
            default:
                Console.WriteLine("Невідома команда."); break;
        }
    }
    Disconnect();
}

bool IsOpen() => db is { State: ConnectionState.Open };

void Require(Action a)
{
    if (!IsOpen())
    {
        Console.WriteLine("Спочатку підключіться до бази (п. 1).");
        return;
    }
    a();
}

async Task ConnectAsync()
{
    await EnsureSchemaAsync(Cs);
    try
    {
        if (db != null)
        {
            await db.CloseAsync();
            await db.DisposeAsync();
            db = null;
        }
        var c = new SqlConnection(Cs);
        await c.OpenAsync();
        db = c;
        var srv = db.DataSource;
        var name = db.Database;
        var ver = db.ServerVersion;
        Console.WriteLine($"Підключення успішне. Сервер: {srv}. База даних: «{name}». Версія сервера: {ver}. Стан: {db.State}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Не вдалося підключитися: {ex.Message}");
        if (db != null)
        {
            try { await db.DisposeAsync(); } catch { /* ignore */ }
            db = null;
        }
    }
}

void Disconnect()
{
    if (db == null)
    {
        Console.WriteLine("З’єднання вже немає.");
        return;
    }
    try
    {
        if (db.State == ConnectionState.Open) db.Close();
        db.Dispose();
        Console.WriteLine("Від’єднано від бази даних.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Помилка під час від’єднання: {ex.Message}");
    }
    finally
    {
        db = null;
    }
}

static async Task EnsureSchemaAsync(string cs)
{
    var master = new SqlConnection(
        @"Server=(localdb)\mssqllocaldb;Database=master;Integrated Security=true;TrustServerCertificate=true;");
    await master.OpenAsync();
    await master.ExecuteAsync(
        "IF DB_ID(N'MailingListPromoUkDb') IS NULL CREATE DATABASE MailingListPromoUkDb;");
    await master.DisposeAsync();

    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    var ddl =
        "IF OBJECT_ID(N'dbo.Countries',N'U') IS NULL CREATE TABLE dbo.Countries (Id INT IDENTITY PRIMARY KEY, Name NVARCHAR(200) NOT NULL);" +
        "IF OBJECT_ID(N'dbo.Cities',N'U') IS NULL CREATE TABLE dbo.Cities (Id INT IDENTITY PRIMARY KEY, Name NVARCHAR(200) NOT NULL, CountryId INT NOT NULL REFERENCES dbo.Countries(Id));" +
        "IF OBJECT_ID(N'dbo.Sections',N'U') IS NULL CREATE TABLE dbo.Sections (Id INT IDENTITY PRIMARY KEY, Title NVARCHAR(200) NOT NULL);" +
        "IF OBJECT_ID(N'dbo.Buyers',N'U') IS NULL CREATE TABLE dbo.Buyers (Id INT IDENTITY PRIMARY KEY, FullName NVARCHAR(250) NOT NULL, BirthDate DATE NOT NULL, Gender NVARCHAR(30) NOT NULL, Email NVARCHAR(260) NOT NULL, CityId INT NOT NULL REFERENCES dbo.Cities(Id));" +
        "IF OBJECT_ID(N'dbo.BuyerSections',N'U') IS NULL CREATE TABLE dbo.BuyerSections (BuyerId INT NOT NULL REFERENCES dbo.Buyers(Id) ON DELETE CASCADE, SectionId INT NOT NULL REFERENCES dbo.Sections(Id) ON DELETE CASCADE, PRIMARY KEY(BuyerId, SectionId));" +
        "IF OBJECT_ID(N'dbo.Promotions',N'U') IS NULL CREATE TABLE dbo.Promotions (Id INT IDENTITY PRIMARY KEY, ProductTitle NVARCHAR(320) NOT NULL, SectionId INT NOT NULL REFERENCES dbo.Sections(Id), CountryId INT NOT NULL REFERENCES dbo.Countries(Id), StartDate DATE NOT NULL, EndDate DATE NOT NULL, CONSTRAINT CK_Promotions_Dates CHECK (EndDate >= StartDate));";

    await conn.ExecuteAsync(ddl);
}

static void ShowBuyers(SqlConnection connection)
{
    var sql = @"
SELECT b.Id, b.FullName, b.BirthDate, b.Gender, b.Email, ct.Name AS CityName, co.Name AS CountryName
FROM dbo.Buyers b
JOIN dbo.Cities ct ON ct.Id=b.CityId
JOIN dbo.Countries co ON co.Id=ct.CountryId
ORDER BY b.Id;";
    var rows = connection.Query<BuyerRow>(sql);
    if (!rows.Any()) { Console.WriteLine("Покупців немає."); return; }
    foreach (var r in rows)
        Console.WriteLine($"{r.Id}. {r.FullName} | {r.BirthDate:yyyy-MM-dd} | {r.Gender} | {r.Email} | {r.CityName}, {r.CountryName}");
}

static void ShowEmails(SqlConnection connection)
{
    var rows = connection.Query<string>("SELECT Email FROM dbo.Buyers ORDER BY Id;");
    if (!rows.Any()) { Console.WriteLine("Покупців немає."); return; }
    foreach (var e in rows) Console.WriteLine(e);
}

static void ShowSections(SqlConnection connection)
{
    var rows = connection.Query<SectionRow>("SELECT Id, Title FROM dbo.Sections ORDER BY Id;");
    if (!rows.Any()) { Console.WriteLine("Розділів немає."); return; }
    foreach (var s in rows) Console.WriteLine($"{s.Id}. {s.Title}");
}

static void ShowPromos(SqlConnection connection)
{
    var sql = @"
SELECT p.Id, p.ProductTitle, s.Title AS SectionTitle, co.Name AS CountryName, p.StartDate, p.EndDate
FROM dbo.Promotions p
JOIN dbo.Sections s ON s.Id=p.SectionId
JOIN dbo.Countries co ON co.Id=p.CountryId
ORDER BY p.Id;";
    var rows = connection.Query<PromoRow>(sql);
    if (!rows.Any()) { Console.WriteLine("Акційних пропозицій немає."); return; }
    foreach (var p in rows)
        Console.WriteLine($"{p.Id}. {p.ProductTitle} | Розділ: {p.SectionTitle} | Країна: {p.CountryName} | {p.StartDate:yyyy-MM-dd} — {p.EndDate:yyyy-MM-dd}");
}

static void ShowCities(SqlConnection connection)
{
    var sql = @"
SELECT c.Id, c.Name AS CityName, co.Name AS CountryName
FROM dbo.Cities c JOIN dbo.Countries co ON co.Id=c.CountryId
ORDER BY co.Name, c.Name;";
    var rows = connection.Query<CityRow>(sql);
    if (!rows.Any()) { Console.WriteLine("Міст немає."); return; }
    foreach (var c in rows) Console.WriteLine($"{c.Id}. {c.CityName}, {c.CountryName}");
}

static void ShowCountries(SqlConnection connection)
{
    var rows = connection.Query<CountryRow>("SELECT Id, Name FROM dbo.Countries ORDER BY Name;");
    if (!rows.Any()) { Console.WriteLine("Країн немає."); return; }
    foreach (var c in rows) Console.WriteLine($"{c.Id}. {c.Name}");
}

static void ShowBuyersByCity(SqlConnection connection)
{
    Console.Write("Ідентифікатор міста: ");
    if (!int.TryParse(Console.ReadLine(), out var cityId)) { Console.WriteLine("Некоректний ідентифікатор."); return; }
    var sql = @"
SELECT b.Id, b.FullName, b.BirthDate, b.Gender, b.Email, ct.Name AS CityName, co.Name AS CountryName
FROM dbo.Buyers b
JOIN dbo.Cities ct ON ct.Id=b.CityId
JOIN dbo.Countries co ON co.Id=ct.CountryId
WHERE ct.Id=@cityId
ORDER BY b.FullName;";
    var rows = connection.Query<BuyerRow>(sql, new { cityId });
    if (!rows.Any()) { Console.WriteLine("У цьому місті покупців не знайдено."); return; }
    foreach (var r in rows)
        Console.WriteLine($"{r.Id}. {r.FullName} | {r.BirthDate:yyyy-MM-dd} | {r.Gender} | {r.Email} | {r.CityName}, {r.CountryName}");
}

static void ShowBuyersByCountry(SqlConnection connection)
{
    Console.Write("Ідентифікатор країни: ");
    if (!int.TryParse(Console.ReadLine(), out var countryId)) { Console.WriteLine("Некоректний ідентифікатор."); return; }
    var sql = @"
SELECT b.Id, b.FullName, b.BirthDate, b.Gender, b.Email, ct.Name AS CityName, co.Name AS CountryName
FROM dbo.Buyers b
JOIN dbo.Cities ct ON ct.Id=b.CityId
JOIN dbo.Countries co ON co.Id=ct.CountryId
WHERE co.Id=@countryId
ORDER BY ct.Name, b.FullName;";
    var rows = connection.Query<BuyerRow>(sql, new { countryId });
    if (!rows.Any()) { Console.WriteLine("У цій країні покупців не знайдено."); return; }
    foreach (var r in rows)
        Console.WriteLine($"{r.Id}. {r.FullName} | {r.BirthDate:yyyy-MM-dd} | {r.Gender} | {r.Email} | {r.CityName}, {r.CountryName}");
}

static void ShowPromosByCountry(SqlConnection connection)
{
    Console.Write("Ідентифікатор країни: ");
    if (!int.TryParse(Console.ReadLine(), out var countryId)) { Console.WriteLine("Некоректний ідентифікатор."); return; }
    var sql = @"
SELECT p.Id, p.ProductTitle, s.Title AS SectionTitle, co.Name AS CountryName, p.StartDate, p.EndDate
FROM dbo.Promotions p
JOIN dbo.Sections s ON s.Id=p.SectionId
JOIN dbo.Countries co ON co.Id=p.CountryId
WHERE p.CountryId=@countryId
ORDER BY p.StartDate, p.ProductTitle;";
    var rows = connection.Query<PromoRow>(sql, new { countryId });
    if (!rows.Any()) { Console.WriteLine("Для цієї країни акційних пропозицій не знайдено."); return; }
    foreach (var p in rows)
        Console.WriteLine($"{p.Id}. {p.ProductTitle} | Розділ: {p.SectionTitle} | Країна: {p.CountryName} | {p.StartDate:yyyy-MM-dd} — {p.EndDate:yyyy-MM-dd}");
}

internal sealed record BuyerRow
{
    public int Id { get; init; }
    public string FullName { get; init; } = "";
    public DateTime BirthDate { get; init; }
    public string Gender { get; init; } = "";
    public string Email { get; init; } = "";
    public string CityName { get; init; } = "";
    public string CountryName { get; init; } = "";
}

internal sealed record SectionRow { public int Id { get; init; } public string Title { get; init; } = ""; }
internal sealed record CountryRow { public int Id { get; init; } public string Name { get; init; } = ""; }
internal sealed record CityRow { public int Id { get; init; } public string CityName { get; init; } = ""; public string CountryName { get; init; } = ""; }
internal sealed record PromoRow
{
    public int Id { get; init; }
    public string ProductTitle { get; init; } = "";
    public string SectionTitle { get; init; } = "";
    public string CountryName { get; init; } = "";
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
}