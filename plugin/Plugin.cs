using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DiceMaster;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    private readonly WindowSystem windows = new("DiceMaster");
    private readonly RelayClient relay;
    private readonly MainWindow main;
    private readonly DiceWindow dice;
    private readonly Configuration config;
    public Plugin()
    {
        config = Interface.GetPluginConfig() as Configuration ?? new Configuration();
        if (string.IsNullOrWhiteSpace(config.RelayUrl)) config.RelayUrl = Configuration.DefaultRelayUrl;
        relay = new RelayClient(new CredentialStore(Interface.GetPluginConfigDirectory()));
        dice = new DiceWindow(relay, config);
        main = new MainWindow(config, relay, () => Interface.SavePluginConfig(config), () => dice.IsOpen = true);
        windows.AddWindow(main); windows.AddWindow(dice);
        Commands.AddHandler("/dicemaster", new CommandInfo((_, _) => Open()) { HelpMessage = "Open DiceMaster." });
        Interface.UiBuilder.Draw += Draw;
        Interface.UiBuilder.OpenMainUi += Open;
        Interface.UiBuilder.OpenConfigUi += Open;
    }
    private void Draw() { relay.Drain(); using var appearance = new Appearance(config); windows.Draw(); }
    private void Open() { main.IsOpen = true; }
    public void Dispose()
    {
        Interface.UiBuilder.Draw -= Draw;
        Interface.UiBuilder.OpenMainUi -= Open;
        Interface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/dicemaster"); windows.RemoveAllWindows(); relay.Dispose();
    }
}
