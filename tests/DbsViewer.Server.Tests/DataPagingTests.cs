using System.Net;
using System.Net.Http.Json;
using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Stránkování, řazení a filtrování proti běžící aplikaci a skutečné databázi.
/// Skládání SQL testuje <see cref="DataQueryBuilderTests"/>; tady jde o to, že dotaz
/// databáze opravdu přijme a vrátí, co se čeká.
/// </summary>
public class DataPagingTests
{
    private const string Url = "/dbschema/api/tables/-/Customers/rows";

    private static async Task<DbsViewerApp> StartAsync(int radku, int maxRows = 100)
    {
        var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.Enabled = true;
            o.DataPreview.MaxRows = maxRows;
        });

        for (var i = 0; i < radku; i++)
        {
            await app.ExecuteAsync(
                "INSERT INTO Customers (Email, DisplayName, CreatedAt) "
                + $"VALUES ('u{i:00}@x.cz', 'Zakaznik {i:00}', '2026-01-01')");
        }

        return app;
    }

    private static async Task<DataPreview> LoadAsync(DbsViewerApp app, DataQuery query)
    {
        var response = await app.Client.PostAsJsonAsync(Url, query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<DataPreview>()
            ?? throw new InvalidOperationException("Prázdná odpověď.");
    }

    [Fact]
    public async Task Prvni_stranka_se_nacte_i_bez_dotazu()
    {
        await using var app = await StartAsync(5);

        var preview = await LoadAsync(app, new DataQuery { PageSize = 10 });

        Assert.Equal(5, preview.Rows.Count);
        Assert.Equal(0, preview.Page);
        Assert.Equal(5, preview.TotalRows);
        Assert.Equal(1, preview.PageCount);
        Assert.False(preview.HasMore);
    }

    [Fact]
    public async Task Druha_stranka_vraci_dalsi_radky()
    {
        await using var app = await StartAsync(25);

        var prvni = await LoadAsync(app, new DataQuery { PageSize = 10 });
        var druha = await LoadAsync(app, new DataQuery { Page = 1, PageSize = 10 });

        Assert.Equal(10, druha.Rows.Count);
        Assert.Equal(3, druha.PageCount);
        Assert.True(druha.HasMore);

        // Stránky se nesmí překrývat — to je celý smysl stabilního řazení.
        Assert.Empty(prvni.Rows.Select(r => r[0]).Intersect(druha.Rows.Select(r => r[0])));
    }

    [Fact]
    public async Task Posledni_stranka_uz_dalsi_nenabizi()
    {
        await using var app = await StartAsync(25);

        var posledni = await LoadAsync(app, new DataQuery { Page = 2, PageSize = 10 });

        Assert.Equal(5, posledni.Rows.Count);
        Assert.False(posledni.HasMore);
    }

    [Fact]
    public async Task Stranka_za_koncem_je_prazdna()
    {
        await using var app = await StartAsync(5);

        var preview = await LoadAsync(app, new DataQuery { Page = 99, PageSize = 10 });

        Assert.Empty(preview.Rows);
        Assert.Equal(5, preview.TotalRows);
    }

    [Fact]
    public async Task Data_se_daji_seradit_sestupne()
    {
        await using var app = await StartAsync(5);

        var query = new DataQuery { SortColumn = "Email", SortDescending = true, PageSize = 10 };
        var preview = await LoadAsync(app, query);

        var emaily = preview.Rows.Select(r => r[Index(preview, "Email")]).ToList();

        Assert.Equal("u04@x.cz", emaily[0]);
        Assert.Equal(emaily.OrderByDescending(static e => e, StringComparer.Ordinal), emaily);
        Assert.Equal("Email", preview.SortColumn);
        Assert.True(preview.SortDescending);
    }

    [Fact]
    public async Task Razeni_podle_neznameho_sloupce_data_nerozbije()
    {
        await using var app = await StartAsync(3);

        var preview = await LoadAsync(app, new DataQuery { SortColumn = "Neexistuje", PageSize = 10 });

        Assert.Equal(3, preview.Rows.Count);
        Assert.Null(preview.SortColumn);
    }

    [Fact]
    public async Task Filtr_omezi_radky_i_celkovy_pocet()
    {
        await using var app = await StartAsync(20);

        var query = new DataQuery
        {
            PageSize = 50,
            Filters = [new DataFilter("Email", FilterOperator.Contains, "u1")],
        };

        var preview = await LoadAsync(app, query);

        // u10 až u19 je deset řádků.
        Assert.Equal(10, preview.Rows.Count);
        Assert.Equal(10, preview.TotalRows);
    }

    [Fact]
    public async Task Filtr_na_presnou_shodu_najde_jeden_radek()
    {
        await using var app = await StartAsync(10);

        var query = new DataQuery
        {
            Filters = [new DataFilter("Email", FilterOperator.Equals, "u03@x.cz")],
        };

        var preview = await LoadAsync(app, query);

        Assert.Single(preview.Rows);
        Assert.Equal(1, preview.TotalRows);
    }

    [Fact]
    public async Task Filtr_bez_shody_vrati_prazdnou_stranku()
    {
        await using var app = await StartAsync(5);

        var query = new DataQuery
        {
            Filters = [new DataFilter("Email", FilterOperator.Contains, "nikdo")],
        };

        var preview = await LoadAsync(app, query);

        Assert.Empty(preview.Rows);
        Assert.Equal(0, preview.TotalRows);

        // Sloupce musí dorazit i u prázdného výsledku, jinak by se rozpadla hlavička.
        Assert.NotEmpty(preview.Columns);
    }

    [Fact]
    public async Task Zastupny_znak_v_hodnote_nehleda_cokoli()
    {
        await using var app = await StartAsync(5);

        // Kdyby se „%" nescapovalo, LIKE by našel všech pět řádků.
        var query = new DataQuery
        {
            Filters = [new DataFilter("Email", FilterOperator.Contains, "%")],
        };

        Assert.Equal(0, (await LoadAsync(app, query)).TotalRows);
    }

    [Fact]
    public async Task Filtr_funguje_i_nad_ciselnym_sloupcem()
    {
        await using var app = await StartAsync(12);

        // Sloupec Id je číslo; hledá se v něm textově, protože v mřížce se typ nerozlišuje.
        var query = new DataQuery
        {
            Filters = [new DataFilter("Id", FilterOperator.Contains, "1")],
        };

        var preview = await LoadAsync(app, query);

        Assert.True(preview.TotalRows > 0);
    }

    [Fact]
    public async Task Filtry_se_kombinuji_pres_AND()
    {
        await using var app = await StartAsync(20);

        var query = new DataQuery
        {
            PageSize = 50,
            Filters =
            [
                new DataFilter("Email", FilterOperator.Contains, "u1"),
                new DataFilter("DisplayName", FilterOperator.EndsWith, "15"),
            ],
        };

        Assert.Equal(1, (await LoadAsync(app, query)).TotalRows);
    }

    [Fact]
    public async Task Velikost_stranky_se_orizne_na_maximum()
    {
        await using var app = await StartAsync(20, maxRows: 5);

        var preview = await LoadAsync(app, new DataQuery { PageSize = 1000 });

        Assert.Equal(5, preview.Rows.Count);
        Assert.Equal(5, preview.PageSize);
        Assert.Equal(4, preview.PageCount);
    }

    [Fact]
    public async Task Zaporna_stranka_se_srovna_na_prvni()
    {
        await using var app = await StartAsync(5);

        var preview = await LoadAsync(app, new DataQuery { Page = -10, PageSize = 10 });

        Assert.Equal(0, preview.Page);
        Assert.Equal(5, preview.Rows.Count);
    }

    [Fact]
    public async Task Filtr_se_nedostane_do_dotazu_jako_SQL()
    {
        await using var app = await StartAsync(5);

        var query = new DataQuery
        {
            Filters = [new DataFilter("Email", FilterOperator.Contains, "'; DROP TABLE Orders;--")],
        };

        var preview = await LoadAsync(app, query);

        Assert.Empty(preview.Rows);

        // Orders musí pořád existovat.
        Assert.Equal("0", await app.ScalarAsync("SELECT COUNT(*) FROM Orders"));
    }

    [Fact]
    public async Task Selhani_poctu_neshodi_nahled()
    {
        // Když COUNT selže, stránkuje se dál — jen bez celkového počtu. Testuje se
        // přímo, protože v běžícím serveru nejde shodit počítání, aniž by se shodilo
        // i čtení stránky.
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Neexistuje";

        Assert.Null(await DataPreviewService.TryCountAsync(command, "T", Logger, default));
    }

    [Fact]
    public async Task Uspesny_pocet_vrati_cislo()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42";

        Assert.Equal(42, await DataPreviewService.TryCountAsync(command, "T", Logger, default));
    }

    [Fact]
    public async Task Pocet_vracejici_NULL_se_bere_jako_nezname()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NULL";

        Assert.Null(await DataPreviewService.TryCountAsync(command, "T", Logger, default));
    }

    [Fact]
    public async Task Pocet_bez_prikazu_nebo_loggeru_je_chyba_argumentu()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await using var command = connection.CreateCommand();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DataPreviewService.TryCountAsync(null!, "T", Logger, default));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DataPreviewService.TryCountAsync(command, "T", null!, default));
    }

    private static Microsoft.Extensions.Logging.ILogger Logger =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static int Index(DataPreview preview, string column) =>
        preview.Columns
            .Select(static (c, i) => (c, i))
            .First(x => string.Equals(x.c, column, StringComparison.OrdinalIgnoreCase))
            .i;
}
