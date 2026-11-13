using System.Globalization;
using System.Windows;
using AqwCompanion.Skua;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace SkuaLink.SkuaBridge;

/// <summary>
/// The thin Skua-bound glue for script options: Skua's <c>Config</c> of the script it started last, the options files in
/// Skua's options folder (<c>ClientFileSources.SkuaOptionsDIR</c>, <c>%AppData%\Skua\options</c>), and Skua's options
/// dialog among its open WPF windows. Every decision (which file, what may be written, when the dialog is closed or stuck)
/// is in <see cref="SkuaCommandHandler"/> and <see cref="SkuaScriptOptions"/>, unit-tested without Skua. Only framework
/// WPF types are used for the windows (no Skua.Core or Skua.WPF reference): the dialog is the window whose
/// <c>DataContext</c> is of type <c>OptionContainerViewModel</c> (by name, through reflection) for the script's
/// <c>Config</c>; it is closed with <c>Window.Close()</c> on Skua's UI thread through Skua's own
/// <see cref="IDispatcherService"/>. Every member may throw; the handler catches it.
/// </summary>
internal sealed class ScriptOptions : ISkuaOptionsHost
{
    /// <summary>A file larger than this is not an options file the plugin reads.</summary>
    private const long MaxFileBytes = 256 * 1024;

    /// <summary>A script larger than this is not searched for its <c>OptionsStorage</c>.</summary>
    private const long MaxSourceBytes = 4 * 1024 * 1024;

    private readonly IScriptStatus _manager;
    private readonly IDispatcherService? _dispatcher;

    public ScriptOptions(IScriptStatus manager, IDispatcherService? dispatcher)
    {
        _manager = manager;
        _dispatcher = dispatcher;
    }

    public string OptionsRoot => ClientFileSources.SkuaOptionsDIR;

    /// <summary>The culture of the plugin's pool threads, as of Skua's script thread: the PC's unless Skua changed the default.</summary>
    public CultureInfo FileCulture => CultureInfo.CurrentCulture;

    /// <summary>Skua replaces <c>Config</c> on every start (<c>LoadScriptConfig</c>), so right after a start it is that script's.</summary>
    public SkuaLoadedOptions? ReadLoadedOptions()
    {
        if (_manager.Config is not { } config)
        {
            return null;
        }

        var options = new List<SkuaOptionDescriptor>();
        foreach (var option in config.Options?.ToList() ?? [])
        {
            Add(options, SkuaOptionsFile.DefaultCategory, option);
        }

        // The dictionary key is the category Skua writes in the file (the option's own Category has spaces for underscores).
        foreach (var (category, list) in config.MultipleOptions?.ToList() ?? [])
        {
            foreach (var option in list?.ToList() ?? [])
            {
                Add(options, category, option);
            }
        }

        return new SkuaLoadedOptions(_manager.LoadedScript, config.OptionsFile, options);
    }

    /// <summary>A script the catalog resolved (inside Skua's scripts folder, no link); only its text is read, never run.</summary>
    public string? ReadScriptSource(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length <= MaxSourceBytes ? File.ReadAllText(path) : null;
    }

    public IReadOnlyList<string>? ReadOptionsFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length > MaxFileBytes)
        {
            throw new IOException("not an options file");
        }

        return File.ReadAllLines(path);
    }

    /// <summary>Written like Skua's own <c>OptionContainer.Save</c> (<c>File.WriteAllLines</c>); a link is never written through.</summary>
    public void WriteOptionsFile(string path, IReadOnlyList<string> lines)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("not an options file");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// Looks at Skua's open windows on its UI thread. <see cref="SkuaOptionsWindow.Unknown"/> when there is no dispatcher
    /// or no WPF application (Skua's dispatcher then does nothing) or the look failed.
    /// </summary>
    public SkuaOptionsWindow FindOptionsWindow()
    {
        var container = _manager.Config;
        var found = SkuaOptionsWindow.Unknown;
        _dispatcher?.Invoke(() =>
        {
            if (Application.Current is { } app)
            {
                found = OptionsDialogs(app, container).Count > 0 ? SkuaOptionsWindow.Open : SkuaOptionsWindow.None;
            }
        });
        return found;
    }

    /// <summary>
    /// Closes the dialog on Skua's UI thread. Skua's dialog runs inside <c>ShowDialog</c>; closing it ends that, and Skua's
    /// <c>Closed</c> handler saves the values the dialog shows (loaded from the options file when it opened).
    /// </summary>
    public void CloseOptionsWindow()
    {
        var container = _manager.Config;
        _dispatcher?.Invoke(() =>
        {
            if (Application.Current is { } app)
            {
                foreach (var window in OptionsDialogs(app, container))
                {
                    window.Close();
                }
            }
        });
    }

    private static List<Window> OptionsDialogs(Application app, object? container) =>
        app.Windows.OfType<Window>().Where(w => SkuaScriptOptions.IsOptionsDialog(w.DataContext, container)).ToList();

    /// <summary>A script's own option object may throw from a getter; that option is left out, the others still count.</summary>
    private static void Add(List<SkuaOptionDescriptor> options, string category, IOption? option)
    {
        if (option is null)
        {
            return;
        }

        try
        {
            options.Add(new(category, option.Name, option.DisplayName, option.Description, option.Type, option.DefaultValue));
        }
        catch (Exception)
        {
            // Left out: setOptions then refuses its name, and the dialog still shows it on the PC.
        }
    }
}
