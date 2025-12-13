namespace Shared.Http;

using Shared.Config;
using System.Net;

public abstract class HttpServer
{
    // Router principal que manejará todas las rutas y middlewares
    protected HttpRouter router;

    // Listener HTTP que escucha conexiones entrantes
    protected HttpListener server;

    // Constructor
    public HttpServer()
    {
        // Inicializa el router
        router = new HttpRouter();

        // Llama al método abstracto Init(), donde las subclases definirán sus rutas y middlewares
        Init();

        // Obtiene host y puerto desde configuración (variables de entorno o archivos cfg)
        string host = Configuration.Get<string>("HOST", "http://127.0.0.1");
        string port = Configuration.Get<string>("PORT", "5000");
        string authority = $"{host}:{port}/";

        // Crea y configura el listener HTTP
        server = new HttpListener();
        server.Prefixes.Add(authority);

        Console.WriteLine("Server started at " + authority);
    }

    // Método abstracto que debe ser implementado por la clase hija
    // Aquí se definen rutas, middlewares y otras inicializaciones del router
    public abstract void Init();

    // Inicia el servidor y entra en bucle de escucha de requests
    public async Task Start()
    {
        server.Start();

        // Bucle infinito mientras el servidor esté escuchando
        while (server.IsListening)
        {
            // Espera un request entrante
            HttpListenerContext ctx = await server.GetContextAsync();

            // Pasa el contexto al router para manejarlo de forma asíncrona
            _ = router.HandleContextAsync(ctx);
        }
    }

    // Detiene el servidor de forma segura
    public void Stop()
    {
        if (server.IsListening)
        {
            server.Stop();
            server.Close();
            Console.WriteLine("Server stopped.");
        }
    }
}