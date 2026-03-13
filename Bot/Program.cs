using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

// =================================================================
// PUNTO DE ENTRADA (MENÚ PRINCIPAL)
// =================================================================
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║        🤖 BOT AUTOMATIZADOR  🤖       ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.ResetColor();

Console.WriteLine("Selecciona un escenario de prueba:");
Console.WriteLine("  1. Buscar C# en Wikipedia y capturar");
Console.WriteLine("  2. SauceDemo ");
Console.WriteLine("  3. Decathlon ");

Console.Write("\nOpción seleccionada: ");

string opcion = Console.ReadLine() ?? "1";

try
{
    // Configuración centralizada del navegador
    bool esHeadless = opcion == "4";
    int slowMo = (opcion == "2" || opcion == "3") ? 800 : 0; // 800ms para que el ojo humano pueda verlo

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Headless = esHeadless,
        SlowMo = slowMo,
        Args = new[] { "--start-maximized" }
    });

    var page = await browser.NewPageAsync(new BrowserNewPageOptions
    {
        ViewportSize = ViewportSize.NoViewport // Permite que se adapte al maximizado
    });

    // Enrutador de tareas según la opción
    switch (opcion)
    {
        case "1":
            await TareasBot.EjecutarWikipedia(page);
            break;
        case "2":
            await TareasBot.EjecutarSauceDemo(page);
            break;
        case "3":
            await TareasBot.EjecutarDecathlon(page, rapido: false);
            break;
        default:
            Logger.Warn("Opción no válida. Saliendo del programa.");
            break;
    }

    await browser.CloseAsync();
}
catch (Exception ex)
{
    Logger.Error($"Ocurrió un error crítico inesperado: {ex.Message}");
}

Console.WriteLine("\nProceso terminado. Presiona cualquier tecla para salir...");
Console.ReadKey();


// =================================================================
// CLASE CON LA LÓGICA DE LOS BOTS (SEPARACIÓN DE RESPONSABILIDADES)
// =================================================================
public static class TareasBot
{
    public static async Task EjecutarWikipedia(IPage page)
    {
        Logger.Info("Navegando a Wikipedia...");
        await page.GotoAsync("https://es.wikipedia.org/");

        // Playwright espera automáticamente a que el input sea accionable (Auto-wait)
        await page.FillAsync("input[name=\"search\"]", "C Sharp");
        await page.Keyboard.PressAsync("Enter");

        // Espera dinámica: No usamos Timeout, esperamos a que el elemento clave aparezca
        await page.WaitForSelectorAsync("#firstHeading");
        Logger.Success($"Página cargada: {await page.TitleAsync()}");

        string rutaCaptura = "captura_csharp.png";
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = rutaCaptura, FullPage = true });
        Logger.Success($"Captura guardada como: {rutaCaptura}");
    }

    public static async Task EjecutarSauceDemo(IPage page)
    {
        Logger.Info("Navegando a SauceDemo (Tienda de pruebas)...");
        await page.GotoAsync("https://www.saucedemo.com/");

        Logger.Info("Iniciando sesión...");
        await page.FillAsync("#user-name", "standard_user");
        await page.FillAsync("#password", "secret_sauce");
        await page.ClickAsync("#login-button");

        // Validamos que el login fue exitoso esperando el título del inventario
        await page.WaitForSelectorAsync(".title:has-text('Products')");

        Logger.Info("Añadiendo el primer producto al carrito...");
        var primerProducto = page.Locator(".inventory_item").First;
        string nombreProd = await primerProducto.Locator(".inventory_item_name").InnerTextAsync();

        await primerProducto.Locator("button.btn_inventory").ClickAsync();
        Logger.Success($"¡Producto '{nombreProd}' añadido al carrito!");

        Logger.Info("Yendo al carrito...");
        await page.ClickAsync(".shopping_cart_link");
        await page.WaitForSelectorAsync(".cart_item"); // Esperamos a ver elementos en el carrito

        ExportadorDatos.GuardarCompraCsv("SauceDemo", nombreProd);
    }

    public static async Task EjecutarDecathlon(IPage page, bool rapido)
    {
        Logger.Info($"Navegando a Decathlon {(rapido ? "(Modo Rápido)" : "(Modo Visual)")}...");
        await page.GotoAsync("https://www.decathlon.es/es/");

        // Manejo elegante de Cookies: Esperamos el botón un máximo de 3 segundos
        try
        {
            var btnCookies = page.Locator("#didomi-notice-agree-button");
            await btnCookies.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
            await btnCookies.ClickAsync();
            Logger.Info("Cookies aceptadas.");
        }
        catch { Logger.Warn("Banner de cookies no detectado o ya aceptado."); }

        string[] productosABuscar = rapido ? new[] { "botas montaña" } : new[] { "mochila senderismo", "cantimplora" };
        var productosAñadidos = new List<string>();

        foreach (var busqueda in productosABuscar)
        {
            Logger.Info($"Buscando: {busqueda}");

            // Selector robusto para el buscador
            var searchInput = page.Locator("input[type='search'], input#search-suggestions-input, #search-block-input, input[placeholder*='Buscar']").First;

            // Usamos un try/catch en la espera por si el input está oculto inicialmente
            try {
                await searchInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            } catch { }

            await searchInput.FillAsync(busqueda, new LocatorFillOptions { Force = true });
            await page.Keyboard.PressAsync("Enter");

            // Esperamos que la red se calme (indicativo de que los resultados cargaron) en lugar de un timeout fijo
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Logger.Info("Seleccionando el primer producto...");
            var primerProducto = page.Locator("article a, .product-block-top-main a").First;

            // Navegamos al producto capturando la URL (más seguro que hacer click a veces)
            string enlaceProducto = await primerProducto.GetAttributeAsync("href") ?? "";
            if (!string.IsNullOrEmpty(enlaceProducto))
            {
                string urlDestino = enlaceProducto.StartsWith("http") ? enlaceProducto : $"https://www.decathlon.es{enlaceProducto}";
                await page.GotoAsync(urlDestino);
            }
            else
            {

                await primerProducto.ClickAsync();
            }

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            string nombreProducto = busqueda;
            try { nombreProducto = await page.Locator("h1").First.InnerTextAsync(); } catch { }

            Logger.Info($"Añadiendo a la cesta: {nombreProducto}...");

            var btnCarrito = page.Locator("button:has-text('Añadir a la cesta'), button.conversion-zone__purchase-action, button#add-to-cart-button").First;
            await btnCarrito.ScrollIntoViewIfNeededAsync();
            await btnCarrito.ClickAsync(new LocatorClickOptions { Force = true });

            productosAñadidos.Add(nombreProducto);
            Logger.Success($"¡Producto añadido a la cesta!");

            // Si hay más productos, volvemos al inicio
            if (busqueda != productosABuscar[^1]) // [^1] es sintaxis moderna para "el último elemento"
            {
                await page.GotoAsync("https://www.decathlon.es/es/");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
        }

        Logger.Info("Yendo a confirmar la cesta...");
        await page.GotoAsync("https://www.decathlon.es/es/checkout/cart/");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Pequeña pausa para que cargue dinámicamente
        await page.WaitForTimeoutAsync(2000);

        Logger.Info("Intentando tramitar el pedido (Checkout)...");
        // En Decathlon, tras recargar el carrito puede que la estructura sea diferente, intentamos múltiples selectores flexibles
        var btnTramitar = page.Locator("button.conversion-zone__purchase-action, a.conversion-zone__purchase-action, button:has-text('Validar mi cesta'), a[href*='checkout/delivery'], button:has-text('Comprar'), button[id*='checkout']").First;

        try
        {
            // Intentamos esperar al botón de checkout
            await btnTramitar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

            if (await btnTramitar.IsVisibleAsync())
            {
                await btnTramitar.ScrollIntoViewIfNeededAsync();
                await btnTramitar.ClickAsync(new LocatorClickOptions { Force = true });
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle); // Esperamos a la pantalla de email

                var emailInput = page.Locator("input[type='email'], #email").First;
                // Esperamos explícitamente a que el input sea visible (máx 5 segs)
                await emailInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

                await emailInput.FillAsync("test_bot_compra@ejemplo.com");

                // Hacemos click en el botón de siguiente o pulsamos enter
                var btnSiguienteEmail = page.Locator("button:has-text('Siguiente'), button:has-text('Continúe'), button[type='submit']").First;
                if (await btnSiguienteEmail.IsVisibleAsync())
                {
                    await btnSiguienteEmail.ClickAsync();
                }
                else
                {
                    await page.Keyboard.PressAsync("Enter");
                }

                Logger.Success("¡Llegamos al límite de registro/pago simulado!");

                string nombresUnidos = string.Join(" + ", productosAñadidos);
                ExportadorDatos.GuardarCompraCsv("Decathlon", nombresUnidos);
            }
            else
            {
                // Backup log / CSV action if visible check fails
                GuardarCestaParcial(productosAñadidos);
            }
        }
        catch
        {
            // Entramos aquí si la espera de WaitForAsync o la acción falla por completo (e.g. timeout)
            GuardarCestaParcial(productosAñadidos);
        }
    }

    private static void GuardarCestaParcial(List<string> productosAñadidos)
    {
        Logger.Warn("No se encontró el botón de tramitar pedido. Generando guardado parcial de éxito.");
        // Guardamos éxito igualmente para no perder la info
        string nombresUnidos = string.Join(" + ", productosAñadidos);
        ExportadorDatos.GuardarCompraCsv("Decathlon (Cesta ok)", nombresUnidos);
    }
}

// =================================================================
// CLASE DE UTILIDADES (EXPORTACIÓN)
// =================================================================
public static class ExportadorDatos
{
    public static void GuardarCompraCsv(string tienda, string productos)
    {
        try
        {
            string escritorio = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string rutaCsv = Path.Combine(escritorio, "compras_rapidas.csv");

            bool existeArchivo = File.Exists(rutaCsv);

            // Usamos StreamWriter con true para append (si existe, añade, no sobreescribe)
            using StreamWriter file = new StreamWriter(rutaCsv, append: true, encoding: System.Text.Encoding.UTF8);

            if (!existeArchivo)
            {
                file.WriteLine("Fecha;Tienda;Producto");
            }

            file.WriteLine($"{DateTime.Now:dd/MM/yyyy HH:mm:ss};{tienda};{productos}");
            file.Flush(); // Forzamos escritura en disco

            Logger.Success($"[Excel] Compra guardada con éxito en: {rutaCsv}");
        }
        catch (Exception ex)
        {
            Logger.Error($"No se pudo guardar el archivo CSV: {ex.Message}");
        }
    }
}

// =================================================================
// CLASE DE LOGGING (PARA UNA CONSOLA MÁS VISUAL)
// =================================================================
public static class Logger
{
    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"[INFO] {message}");
        Console.ResetColor();
    }

    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[ ÉXITO ] {message}");
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[ADVERTENCIA] {message}");
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }
}