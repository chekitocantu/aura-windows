using System.Globalization;

namespace Aura.Core;

/// <summary>Spanish and English text for the whole interface.</summary>
public static class Strings
{
    public static string Language { get; private set; } = "es";

    public static void Init(string setting)
    {
        Language = setting switch
        {
            "es" => "es",
            "en" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en",
        };
    }

    public static string T(string key)
    {
        if (Table.TryGetValue(key, out var pair))
            return Language == "es" ? pair.Es : pair.En;
        return key;
    }

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    private static readonly Dictionary<string, (string Es, string En)> Table = new()
    {
        // navigation
        ["nav.library"] = ("Biblioteca", "Library"),
        ["nav.explore"] = ("Explorar", "Explore"),
        ["nav.screens"] = ("Pantallas", "Screens"),
        ["nav.settings"] = ("Ajustes", "Settings"),

        // now playing
        ["now.title"] = ("Reproduciendo", "Now playing"),
        ["now.none"] = ("Sin fondo animado", "No live wallpaper"),
        ["now.paused"] = ("En pausa", "Paused"),
        ["now.autopaused"] = ("En pausa automática", "Auto-paused"),
        ["now.pause"] = ("Pausar", "Pause"),
        ["now.resume"] = ("Reanudar", "Resume"),
        ["now.stop"] = ("Quitar", "Remove"),

        // library
        ["lib.title"] = ("Tu biblioteca", "Your library"),
        ["lib.subtitle"] = ("{0} fondos en {1}", "{0} wallpapers in {1}"),
        ["lib.search"] = ("Buscar en tu biblioteca…", "Search your library…"),
        ["lib.all"] = ("Todos", "All"),
        ["lib.videos"] = ("Animados", "Live"),
        ["lib.images"] = ("Imágenes", "Images"),
        ["lib.add"] = ("Agregar archivos", "Add files"),
        ["lib.openfolder"] = ("Abrir carpeta", "Open folder"),
        ["lib.empty.title"] = ("Tu biblioteca está vacía", "Your library is empty"),
        ["lib.empty.body"] = ("Descarga fondos animados en Explorar o agrega tus propios videos.",
                              "Download live wallpapers from Explore or add your own videos."),
        ["lib.empty.cta"] = ("Ir a Explorar", "Go to Explore"),
        ["lib.apply"] = ("Aplicar", "Apply"),
        ["lib.applyall"] = ("Aplicar en todas las pantallas", "Apply to all screens"),
        ["lib.applyon"] = ("Aplicar en {0}", "Apply on {0}"),
        ["lib.reveal"] = ("Mostrar en el Explorador", "Show in File Explorer"),
        ["lib.delete"] = ("Mandar a la papelera", "Move to Recycle Bin"),
        ["lib.delete.confirm"] = ("¿Mandar «{0}» a la papelera?", "Move \"{0}\" to the Recycle Bin?"),
        ["lib.target"] = ("Aplicar en:", "Apply on:"),
        ["lib.target.all"] = ("Todas las pantallas", "All screens"),
        ["lib.dialog.filter"] = ("Fondos (videos e imágenes)", "Wallpapers (videos and images)"),
        ["lib.active"] = ("Activo", "Active"),

        // explore
        ["exp.title"] = ("Explorar", "Explore"),
        ["exp.subtitle"] = ("Fondos gratis de internet. Descárgalos con un clic.", "Free wallpapers from the web. One click to download."),
        ["exp.search"] = ("Buscar…", "Search…"),
        ["exp.go"] = ("Buscar", "Search"),
        ["exp.more"] = ("Cargar más", "Load more"),
        ["exp.loading"] = ("Cargando…", "Loading…"),
        ["exp.error"] = ("No se pudo cargar: {0}", "Could not load: {0}"),
        ["exp.noresults"] = ("No hay resultados.", "No results."),
        ["exp.download"] = ("Descargar", "Download"),
        ["exp.downloadapply"] = ("Descargar y aplicar", "Download and apply"),
        ["exp.downloaded"] = ("En tu biblioteca", "In your library"),
        ["exp.retry"] = ("Reintentar", "Retry"),
        ["exp.src.motionbgs"] = ("Animados", "Live"),
        ["exp.src.wallhaven"] = ("Wallhaven", "Wallhaven"),
        ["exp.src.bing"] = ("Bing diario", "Bing daily"),
        ["exp.src.minimalistic"] = ("Minimalistas", "Minimalistic"),
        ["cat.all"] = ("Todo", "All"),
        ["cat.anime"] = ("Anime", "Anime"),
        ["cat.games"] = ("Videojuegos", "Games"),
        ["cat.nature"] = ("Naturaleza", "Nature"),
        ["cat.space"] = ("Espacio", "Space"),
        ["cat.car"] = ("Autos", "Cars"),
        ["cat.fantasy"] = ("Fantasía", "Fantasy"),
        ["cat.superhero"] = ("Superhéroes", "Superheroes"),
        ["cat.technology"] = ("Tecnología", "Technology"),
        ["cat.general"] = ("General", "General"),
        ["sort.toplist"] = ("Más populares", "Top"),
        ["sort.hot"] = ("Tendencia", "Trending"),
        ["sort.random"] = ("Al azar", "Random"),
        ["res.all"] = ("Cualquier tamaño", "Any size"),
        ["res.4k"] = ("4K", "4K"),
        ["res.2k"] = ("2K", "2K"),
        ["res.ultrawide"] = ("Ultrawide", "Ultrawide"),

        // screens
        ["scr.title"] = ("Pantallas", "Screens"),
        ["scr.subtitle"] = ("Elige un fondo distinto para cada monitor.", "Pick a different wallpaper for each monitor."),
        ["scr.primary"] = ("Principal", "Primary"),
        ["scr.change"] = ("Cambiar fondo", "Change wallpaper"),
        ["scr.clear"] = ("Quitar", "Remove"),
        ["scr.none"] = ("Sin fondo de Aura", "No Aura wallpaper"),
        ["scr.name"] = ("Pantalla {0}", "Screen {0}"),

        // settings
        ["set.title"] = ("Ajustes", "Settings"),
        ["set.general"] = ("General", "General"),
        ["set.language"] = ("Idioma", "Language"),
        ["set.language.auto"] = ("Automático", "Automatic"),
        ["set.startup"] = ("Abrir Aura al iniciar Windows", "Open Aura when Windows starts"),
        ["set.startup.hint"] = ("Vuelve a poner tu fondo animado al prender la PC.", "Restores your live wallpaper when the PC starts."),
        ["set.folder"] = ("Carpeta de la biblioteca", "Library folder"),
        ["set.folder.change"] = ("Cambiar…", "Change…"),
        ["set.playback"] = ("Reproducción", "Playback"),
        ["set.fit"] = ("Ajuste del video", "Video fit"),
        ["fit.fill"] = ("Llenar (recorta)", "Fill (crop)"),
        ["fit.fit"] = ("Ajustar (bordes negros)", "Fit (black bars)"),
        ["fit.stretch"] = ("Estirar", "Stretch"),
        ["set.mute"] = ("Silenciar el sonido de los fondos", "Mute wallpaper sound"),
        ["set.volume"] = ("Volumen", "Volume"),
        ["set.hwdec"] = ("Usar la tarjeta de video para decodificar", "Use the graphics card to decode"),
        ["set.hwdec.hint"] = ("Gasta menos CPU y batería. Apágalo solo si el video se ve mal.", "Uses less CPU and battery. Turn off only if video looks wrong."),
        ["set.quality"] = ("Calidad al descargar animados", "Quality for live downloads"),
        ["q.4k"] = ("4K cuando exista", "4K when available"),
        ["q.hd"] = ("1080p (archivos más chicos)", "1080p (smaller files)"),
        ["set.pause"] = ("Pausa automática", "Auto-pause"),
        ["set.pause.fullscreen"] = ("Pausar con juegos o ventanas en pantalla completa", "Pause during games or full-screen windows"),
        ["set.pause.maximized"] = ("Pausar cuando una ventana maximizada tapa el escritorio", "Pause when a maximized window covers the desktop"),
        ["set.pause.battery"] = ("Pausar cuando la laptop usa la batería", "Pause when the laptop runs on battery"),
        ["set.pause.lock"] = ("Pausar con la sesión bloqueada", "Pause while the session is locked"),
        ["set.about"] = ("Acerca de", "About"),
        ["set.about.body"] = ("Aura para Windows {0}. Inspirada en Aura de antwny (COSMIC). Videos con mpv.",
                              "Aura for Windows {0}. Inspired by antwny's Aura (COSMIC). Video by mpv."),
        ["set.logs"] = ("Abrir registros", "Open logs"),

        // tray
        ["tray.open"] = ("Abrir Aura", "Open Aura"),
        ["tray.pause"] = ("Pausar", "Pause"),
        ["tray.resume"] = ("Reanudar", "Resume"),
        ["tray.stop"] = ("Quitar fondo animado", "Remove live wallpaper"),
        ["tray.exit"] = ("Salir", "Exit"),
        ["tray.hint.title"] = ("Aura sigue abierta", "Aura is still running"),
        ["tray.hint.body"] = ("Tu fondo sigue aquí. Abre Aura desde el icono junto al reloj.",
                              "Your wallpaper keeps playing. Open Aura from the icon next to the clock."),

        // errors
        ["err.mpv"] = ("No se encontró el reproductor de video (mpv). Reinstala Aura.", "The video player (mpv) is missing. Reinstall Aura."),
        ["err.apply"] = ("No se pudo poner el fondo: {0}", "Could not set the wallpaper: {0}"),
        ["err.download"] = ("Falló la descarga: {0}", "Download failed: {0}"),
        ["err.missing"] = ("El archivo ya no existe: {0}", "The file no longer exists: {0}"),
    };
}
