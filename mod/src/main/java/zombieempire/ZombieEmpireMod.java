package zombieempire;

import java.io.Reader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

import net.minecraft.client.Minecraft;
import net.minecraft.client.server.IntegratedServer;
import net.minecraft.core.Holder;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.sounds.Music;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.world.level.GameType;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.loading.FMLPaths;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import net.neoforged.neoforge.client.event.SelectMusicEvent;
import net.neoforged.neoforge.common.NeoForge;

/**
 * Zombie Empire (client only):
 *  - its own soundtrack in the menu and in game (assets/zombieempire/sounds);
 *  - welcome title on first join, when ZombieEmpire.exe asks for it;
 *  - automatically opens the singleplayer world to LAN.
 * Settings in config/zombieempire.properties, written by ZombieEmpire.exe.
 */
@Mod("zombieempire")
public class ZombieEmpireMod {
    private static final int DELAY_TICKS = 40; // ~2 seconds after joining the world

    private static final Music MENU_MUSIC = music("music.menu", 20, 100);
    private static final Music GAME_MUSIC = music("music.game", 200, 900);

    private final Properties cfg = loadConfig();
    private static final int TITLE_FADE_IN = 20, TITLE_STAY = 100, TITLE_FADE_OUT = 30;

    private boolean welcomeShown;
    private int welcomeTicksLeft;
    private boolean lanHandled;
    private int ticks;

    public ZombieEmpireMod() {
        NeoForge.EVENT_BUS.addListener(ClientTickEvent.Post.class, this::onClientTick);
        if (flag("music", true)) NeoForge.EVENT_BUS.addListener(SelectMusicEvent.class, this::onSelectMusic);
    }

    private static Music music(String path, int minDelay, int maxDelay) {
        SoundEvent event = SoundEvent.createVariableRangeEvent(ResourceLocation.fromNamespaceAndPath("zombieempire", path));
        return new Music(Holder.direct(event), minDelay, maxDelay, true);
    }

    private void onSelectMusic(SelectMusicEvent event) {
        event.setMusic(Minecraft.getInstance().player == null ? MENU_MUSIC : GAME_MUSIC);
    }

    private void onClientTick(ClientTickEvent.Post event) {
        Minecraft mc = Minecraft.getInstance();
        if (mc.player == null) {
            ticks = 0;
            lanHandled = false;
            return;
        }
        if (++ticks < DELAY_TICKS) return;

        if (!welcomeShown) {
            welcomeShown = true; // once per game session
            if (flag("welcome", false)) {
                mc.gui.setTimes(TITLE_FADE_IN, TITLE_STAY, TITLE_FADE_OUT);
                mc.gui.setSubtitle(Component.literal(cfg.getProperty("welcome_subtitle", "")));
                mc.gui.setTitle(Component.literal(cfg.getProperty("welcome_title", "§4§lWelcome to Zombie Empire")));
                welcomeTicksLeft = TITLE_FADE_IN + TITLE_STAY + TITLE_FADE_OUT + 5;
            }
        }
        // Don't rely on Minecraft's title timer: in the modpack the title stayed on screen
        // forever. We clear it ourselves when the time is up.
        if (welcomeTicksLeft > 0 && --welcomeTicksLeft == 0) {
            mc.gui.clear();
            mc.gui.resetTitleTimes();
        }

        if (!lanHandled) {
            lanHandled = true;
            openLan(mc);
        }
    }

    private void openLan(Minecraft mc) {
        IntegratedServer server = mc.getSingleplayerServer();
        if (server == null || server.isPublished() || !flag("lan", true)) return;
        int port = Integer.parseInt(cfg.getProperty("lan_port", "25565").trim());
        GameType mode = GameType.byName(cfg.getProperty("lan_gamemode", "adventure").trim(), GameType.ADVENTURE);
        boolean cheats = flag("lan_cheats", false);

        boolean ok = server.publishServer(mode, cheats, port);
        mc.gui.getChat().addMessage(Component.literal(ok
                ? "§a[Zombie Empire] World opened to LAN on port " + port
                : "§c[Zombie Empire] Could not open LAN on port " + port + " (port already in use?)"));
    }

    private boolean flag(String key, boolean def) {
        return Boolean.parseBoolean(cfg.getProperty(key, Boolean.toString(def)).trim());
    }

    private static Properties loadConfig() {
        Properties p = new Properties();
        Path file = FMLPaths.CONFIGDIR.get().resolve("zombieempire.properties");
        if (Files.exists(file)) {
            try (Reader r = Files.newBufferedReader(file)) {
                p.load(r);
            } catch (Exception ignored) {
            }
        }
        return p;
    }
}
