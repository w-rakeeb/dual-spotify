using System;
using System.IO;
using System.Web.Script.Serialization;

public sealed class Preferences
{
    public string ThemeMode = "Dark";
    public bool CloseToTray, SecondTray = true, SeparateTaskbar = true;
    public bool KeepSecondRunning = true, AutoUpdates = true, ExpandProcesses = true;
    public bool StartSecondWithWindows;
    public bool StartSecondMinimized;
    public static Preferences Current = new Preferences();

    public static void Load(string folder)
    {
        Current = new Preferences();
        var file = Path.Combine(folder, "preferences.json");
        if (File.Exists(file))
        {
            Current = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(file));
            if (Current == null)
                throw new IOException("Launcher preferences are unreadable.");
            return;
        }
        var legacy = Path.Combine(folder, "Preferences.txt");
        if (!File.Exists(legacy))
            return;
        var lines = File.ReadAllLines(legacy);
        if (lines.Length != 3)
            return;
        if (lines[0] == "Dark" || lines[0] == "Light" || lines[0] == "System")
            Current.ThemeMode = lines[0];
        bool value;
        if (Boolean.TryParse(lines[2], out value))
            Current.SecondTray = value;
    }

    public static void Save()
    {
        Engine.WriteDataText("preferences.json", new JavaScriptSerializer().Serialize(Current));
    }
}
