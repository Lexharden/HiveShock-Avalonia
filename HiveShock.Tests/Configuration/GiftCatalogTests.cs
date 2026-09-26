using System.Text.Json;
using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

public class GiftReferenceCatalogTests
{
    [Fact]
    public void Reads_the_tiktok_gifts_format_and_tolerates_variants()
    {
        const string json = """
        [
          { "id": 9427, "nombre_en": "Pegasus", "diamantes": 42999, "icon_url": "https://x/p.webp", "imagen_local": "gift_images\\9427_Pegasus.webp" },
          { "id": "5655", "name": "Rose", "diamonds": "1", "image": "gifts-images/5655_Rose.webp" },
          { "nombre_en": "sin id" },
          { "id": 7096, "nombre_en": "It’s corn", "diamantes": 1 },
        ]
        """;

        var catalog = GiftReferenceCatalog.FromEntries(GiftReferenceCatalog.Parse(json));

        Assert.Equal(3, catalog.Count);
        var pegasus = catalog.FindById("9427")!;
        Assert.Equal(("Pegasus", 42999), (pegasus.Name, pegasus.Diamonds));
        Assert.Equal("gift_images\\9427_Pegasus.webp", pegasus.ImageFile);
        Assert.Equal(1, catalog.FindById("5655")!.Diamonds);
        Assert.Same(catalog.FindById("7096"), catalog.FindByName("its corn"));
    }

    [Fact]
    public void Also_accepts_an_object_with_a_gifts_list()
    {
        var entries = GiftReferenceCatalog.Parse("""{ "gifts": [ { "id": 1, "nombre_en": "A" } ] }""");
        Assert.Single(entries);
    }
}

/// <summary>Usa la carpeta gifts-images real junto al ejecutable de pruebas, con ids que no existen en TikTok.</summary>
public sealed class GiftImagesTests : IDisposable
{
    private readonly List<string> _created = [];

    public void Dispose()
    {
        foreach (var file in _created.Where(File.Exists))
        {
            File.Delete(file);
        }

        GiftImages.Invalidate();
    }

    private string Create(string fileName)
    {
        GiftImages.EnsureDirectory();
        var path = Path.Combine(GiftImages.DirectoryPath, fileName);
        File.WriteAllText(path, "img");
        _created.Add(path);
        GiftImages.Invalidate();
        return path;
    }

    [Fact]
    public void Finds_images_named_id_underscore_name_by_id_and_by_name()
    {
        var file = Create("990001_Zzz_Test_Falcon.webp");

        Assert.Equal(file, GiftImages.ResolvePath("990001", null));
        Assert.Equal(file, GiftImages.ResolvePath(null, "Zzz Test Falcon"));
        Assert.Equal(file, GiftImages.ResolvePath(null, "Rosa / Zzz Test Falcon")); // alias
    }

    [Fact]
    public void Keeps_supporting_the_old_names()
    {
        var byId = Create("990002.png");
        var bySlug = Create("zzz-old-slug-gift.webp");

        Assert.Equal(byId, GiftImages.ResolvePath("990002", null));
        Assert.Equal(bySlug, GiftImages.ResolvePath(null, "Zzz Old Slug Gift"));
    }

    [Fact]
    public void Explicit_path_with_a_wrong_folder_is_resolved_by_file_name()
    {
        var file = Create("990003_Zzz_Wrong_Folder.webp");

        Assert.Equal(file, GiftImages.ResolvePath(null, null, "gift_images\\990003_Zzz_Wrong_Folder.webp"));
    }

    [Fact]
    public void New_files_are_found_without_restarting()
    {
        Assert.Null(GiftImages.ResolvePath("990004", null));
        var file = Create("990004_Zzz_Added_Later.webp");
        Assert.Equal(file, GiftImages.ResolvePath("990004", null));
    }
}

public sealed class GiftCatalogStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hiveshock-catalog-{Guid.NewGuid():N}");
    private string CatalogPath => Path.Combine(_dir, "gift-catalog.json");

    private static readonly GiftReferenceCatalog Reference = GiftReferenceCatalog.FromEntries(
    [
        new("5655", "Rose", 1, "", "gift_images\\5655_Rose.webp"),
        new("8913", "Rosa", 10, "", "gift_images\\8913_Rosa.webp"),
        new("9427", "Pegasus", 42999, "", "gift_images\\9427_Pegasus.webp"),
    ]);

    public GiftCatalogStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private GiftCatalogStore Store()
    {
        var store = new GiftCatalogStore(CatalogPath, Reference);
        store.Load();
        return store;
    }

    [Fact]
    public void Lists_seen_gifts_plus_reference_ones_enriched()
    {
        var store = Store();
        store.Observe("5655", "Rose", 0);          // visto sin diamantes
        store.Observe("8913", "Roson", 10);        // TikTok lo llamaba distinto
        store.Observe("423360", "RalBatin", 5);    // no está en la lista oficial

        var all = store.ListSorted();
        Assert.Equal(4, all.Count);
        Assert.Equal(1, all.Single(g => g.Id == "5655").Diamonds);          // completado desde la lista
        var rosa = all.Single(g => g.Id == "8913");
        Assert.Equal("Rosa", rosa.NameEn);                                    // manda el nombre oficial
        Assert.Contains("Roson", rosa.Also);                                  // el del live queda de alias
        Assert.True(all.Single(g => g.Id == "9427").IsReferenceOnly);
        Assert.False(all.Single(g => g.Id == "423360").IsReferenceOnly);
        Assert.Equal(3, store.ListSorted(includeReference: false).Count);
        Assert.Equal(3, store.Count);
    }

    [Fact]
    public void Saved_file_only_contains_what_was_learned_not_the_reference()
    {
        var store = Store();
        store.Observe("5655", "Rose", 0);
        store.ListSorted(); // no debe modificar lo guardado

        var saved = File.ReadAllText(CatalogPath);
        Assert.Contains("\"5655\"", saved);
        Assert.DoesNotContain("9427", saved);
        Assert.DoesNotContain("imageFile", saved, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(saved);
        // Los diamantes completados desde la lista oficial solo se ven en la lista, no se escriben.
        Assert.False(doc.RootElement.GetProperty("gifts")[0].TryGetProperty("diamonds", out _));
    }

    [Fact]
    public void Variants_with_same_name_and_price_are_merged_but_different_prices_are_not()
    {
        var reference = GiftReferenceCatalog.FromEntries(
        [
            new("5566", "Mishka Bear", 100, "", ""),
            new("5582", "Mishka Bear", 100, "", ""),
            new("19441", "Freestyle", 1, "", ""),
            new("105795", "Freestyle", 1800, "", ""),
        ]);
        var store = new GiftCatalogStore(CatalogPath, reference);
        store.Load();
        store.Observe("5582", "Mishka Bear", 100); // la variante vista pasa a ser la principal

        var all = store.ListSorted();

        var bear = Assert.Single(all, g => g.NameEn == "Mishka Bear");
        Assert.Equal("5582", bear.Id);
        Assert.Equal(["5566"], bear.OtherIds);
        Assert.Equal(1, bear.Seen);
        Assert.False(bear.IsReferenceOnly);
        Assert.Contains("5566", bear.IdsText);
        Assert.Equal(2, all.Count(g => g.NameEn == "Freestyle")); // 1 y 1800 diamantes: regalos distintos
    }

    [Fact]
    public void Official_name_wins_and_the_live_name_becomes_an_alias()
    {
        var reference = GiftReferenceCatalog.FromEntries([new("5827", "Ice Cream Cone", 1, "", "")]);
        var store = new GiftCatalogStore(CatalogPath, reference);
        store.Load();
        store.Observe("5827", "Finger Heart", 1);

        var gift = store.Find("5827")!;
        Assert.Equal("Ice Cream Cone", gift.NameEn);
        Assert.Contains("Finger Heart", gift.Also);
        Assert.Contains("\"Finger Heart\"", File.ReadAllText(CatalogPath)); // lo guardado no cambia
    }

    [Fact]
    public void Search_matches_name_alias_and_any_id_ignoring_accents()
    {
        var gift = new CatalogGift { Id = "5582", NameEn = "Mishka Bear", NameEs = "Corazón", Also = ["Oso"], OtherIds = ["5566"] };

        Assert.True(gift.Matches(""));
        Assert.True(gift.Matches("mishka"));
        Assert.True(gift.Matches("corazon"));
        Assert.True(gift.Matches("OSO"));
        Assert.True(gift.Matches("5566"));
        Assert.False(gift.Matches("pegasus"));
    }

    [Fact]
    public void Find_works_for_seen_and_reference_gifts()
    {
        var store = Store();
        store.Observe("5655", "Rose", 1);

        Assert.Equal(1, store.Find("5655")!.Seen);
        Assert.Equal("Pegasus", store.Find("9427")!.NameEn);
        Assert.Null(store.Find("000"));
    }

    [Fact]
    public void Corrupt_file_is_recovered_from_backup_and_never_overwritten_with_an_empty_catalog()
    {
        var store = Store();
        store.Observe("5655", "Rose", 1);
        store.Observe("8913", "Rosa", 10);   // el segundo guardado deja el primero como .bak
        File.WriteAllText(CatalogPath, "{ \"gifts\": [ {\"id\": ");  // cierre a mitad de escritura

        var reloaded = Store();

        Assert.True(reloaded.Count >= 1);                                  // recuperado del .bak
        Assert.Single(Directory.GetFiles(_dir, "gift-catalog.corrupt-*.json")); // el dañado se aparta, no se borra
    }

    [Fact]
    public void Corrupt_file_without_backup_is_set_aside_before_saving_again()
    {
        File.WriteAllText(CatalogPath, "esto no es json");
        var store = Store();
        store.Observe("5655", "Rose", 1);

        Assert.Single(Directory.GetFiles(_dir, "gift-catalog.corrupt-*.json"));
        Assert.Equal("esto no es json", File.ReadAllText(Directory.GetFiles(_dir, "gift-catalog.corrupt-*.json")[0]));
        Assert.Equal(1, Store().Count);
    }
}
