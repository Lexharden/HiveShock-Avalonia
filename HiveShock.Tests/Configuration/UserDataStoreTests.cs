using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

/// <summary>
/// Escribe de verdad en la carpeta del usuario y junto al ejecutable de pruebas, con un
/// nombre de archivo único por prueba que se borra al terminar.
/// </summary>
public sealed class UserDataStoreTests : IDisposable
{
    private readonly string _file = $".test-{Guid.NewGuid():N}.json";
    private string UserCopy => Path.Combine(UserDataStore.UserDirectory, _file);
    private string MirrorCopy => Path.Combine(AppPaths.AppDirectory, _file);

    public void Dispose()
    {
        foreach (var path in new[] { UserCopy, MirrorCopy })
        {
            foreach (var suffix in new[] { "", ".bak", ".tmp" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
        }
    }

    private sealed class Prefs
    {
        public int? Deaths { get; set; }
        public double OverlayLeft { get; set; } = double.NaN;
    }

    [Fact]
    public void Saves_two_copies_with_backup_and_supports_NaN()
    {
        UserDataStore.Save(_file, new Prefs { Deaths = 5 });
        Thread.Sleep(20);
        UserDataStore.Save(_file, new Prefs { Deaths = 6 });

        Assert.True(File.Exists(UserCopy) && File.Exists(MirrorCopy));
        Assert.True(File.Exists(UserCopy + ".bak") && File.Exists(MirrorCopy + ".bak"));
        var loaded = UserDataStore.Load<Prefs>(_file)!;
        Assert.Equal(6, loaded.Deaths);
        Assert.True(double.IsNaN(loaded.OverlayLeft));
    }

    [Fact]
    public void Survives_losing_either_folder()
    {
        UserDataStore.Save(_file, new Prefs { Deaths = 7 });

        File.Delete(MirrorCopy);
        Assert.Equal(7, UserDataStore.Load<Prefs>(_file)?.Deaths);

        UserDataStore.Save(_file, new Prefs { Deaths = 8 });
        File.Delete(UserCopy);
        File.Delete(UserCopy + ".bak");
        Assert.Equal(8, UserDataStore.Load<Prefs>(_file)?.Deaths);
    }

    [Fact]
    public void Corrupt_copies_fall_back_to_backup()
    {
        UserDataStore.Save(_file, new Prefs { Deaths = 1 });
        Thread.Sleep(20);
        UserDataStore.Save(_file, new Prefs { Deaths = 2 });
        File.WriteAllText(UserCopy, "{ \"Deaths\": 3, \"Ov");
        File.WriteAllText(MirrorCopy, "");

        Assert.Equal(1, UserDataStore.Load<Prefs>(_file)?.Deaths);
    }

    [Fact]
    public void Nothing_saved_returns_null()
    {
        Assert.Null(UserDataStore.Load<Prefs>(_file));
    }
}
