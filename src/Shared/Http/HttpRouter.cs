namespace Shared.Http;

using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Web;

public class HttpRouter
{
    // Constante para indicar que la respuesta aún no se ha enviado
    public const int RESPONSE_NOT_SENT = 777;

    // Contador global de requests
    private static ulong requestId = 0;

    // Ruta base para el router (puede combinarse con routers anidados)
    private string basePath;

    // Lista de middlewares globales que se ejecutan antes de las rutas
    private List<HttpMiddleware> middlewares;

    // Lista de rutas definidas: (HTTP method, path, middlewares asociados)
    private List<(string, string, HttpMiddleware[])> routes;

    public HttpRouter()
    {
        basePath = string.Empty;
        middlewares = [];
        routes = [];
    }

    // Agrega middlewares globales al router
    public HttpRouter Use(params HttpMiddleware[] middlewares)
    {
        this.middlewares.AddRange(middlewares);
        return this;
    }

    // Define una ruta genérica con un método HTTP
    public HttpRouter Map(string method, string path,
     params HttpMiddleware[] middlewares)
    {
        routes.Add((method.ToUpperInvariant(), path, middlewares));
        return this;
    }

    // Métodos de conveniencia para cada verbo HTTP
    public HttpRouter MapGet(string path, params HttpMiddleware[] middlewares)
        => Map("GET", path, middlewares);

    public HttpRouter MapPost(string path, params HttpMiddleware[] middlewares)
        => Map("POST", path, middlewares);

    public HttpRouter MapPut(string path, params HttpMiddleware[] middlewares)
        => Map("PUT", path, middlewares);

    public HttpRouter MapDelete(string path, params HttpMiddleware[] middlewares)
        => Map("DELETE", path, middlewares);

    // Maneja un contexto HTTP entrante (HttpListenerContext)
    public async Task HandleContextAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var props = new Hashtable();

        // Inicializa el status code y el ID de request
        res.StatusCode = RESPONSE_NOT_SENT;
        props["req.id"] = ++requestId;

        try
        {
            // Ejecuta la cadena de middlewares globales
            await HandleAsync(req, res, props, () => Task.CompletedTask);
        }
        finally
        {
            // Si ningún middleware envió respuesta, usamos NotImplemented
            if (res.StatusCode == RESPONSE_NOT_SENT)
            {
                res.StatusCode = (int)HttpStatusCode.NotImplemented;
            }
            res.Close();
        }
    }

    // Ejecuta los middlewares globales
    private async Task HandleAsync(HttpListenerRequest req,
    HttpListenerResponse res, Hashtable props, Func<Task> next)
    {
        Func<Task> globalMiddlewarePipeline =
            GenerateMiddlewarePipeline(req, res, props, middlewares);
        await globalMiddlewarePipeline();
        await next();
    }

    // Permite montar otro router en una subruta
    public HttpRouter UseRouter(string path, HttpRouter router)
    {
        router.basePath = this.basePath + path;
        return Use(router.HandleAsync);
    }

    // Genera un pipeline de ejecución de middlewares encadenados
    private Func<Task> GenerateMiddlewarePipeline(HttpListenerRequest req,
        HttpListenerResponse res, Hashtable props, List<HttpMiddleware> middlewares)
    {
        int index = -1;
        Func<Task> next = () => Task.CompletedTask;

        next = async () =>
        {
            index++;
            if (index < middlewares.Count && res.StatusCode == RESPONSE_NOT_SENT)
            {
                await middlewares[index](req, res, props, next);
            }
        };

        return next;
    }

    // Agrega middleware para coincidencia de rutas exacta
    public HttpRouter UseSimpleRouteMatching()
    {
        return Use(SimpleRouteMatching);
    }

    // Agrega middleware para rutas con parámetros (ej: /users/:id)
    public HttpRouter UseParametrizedRouteMatching()
    {
        return Use(ParametrizedRouteMatching);
    }

    // Middleware que busca rutas exactas
    private async Task SimpleRouteMatching(HttpListenerRequest req,
        HttpListenerResponse res, Hashtable props, Func<Task> next)
    {
        foreach (var (method, path, middlewares) in routes)
        {
            if (req.HttpMethod == method &&
                string.Equals(req.Url!.AbsolutePath, basePath + path))
            {
                Func<Task> routeMiddlewarePipeline =
                    GenerateMiddlewarePipeline(req, res, props, middlewares.ToList());

                await routeMiddlewarePipeline();

                break; // corta el pipeline global
            }
        }

        await next();
    }

    // Middleware que permite rutas con parámetros (ej: /users/:id)
    private async Task ParametrizedRouteMatching(HttpListenerRequest req,
        HttpListenerResponse res, Hashtable props, Func<Task> next)
    {
        foreach (var (method, path, middlewares) in routes)
        {
            NameValueCollection? parameters;

            if (req.HttpMethod == method &&
                (parameters = ParseUrlParams(req.Url!.AbsolutePath, basePath + path)) != null)
            {
                props["req.params"] = parameters;

                Func<Task> routeMiddlewarePipeline =
                    GenerateMiddlewarePipeline(req, res, props, middlewares.ToList());

                await routeMiddlewarePipeline();

                break; // corta el pipeline global
            }
        }

        await next();
    }

    // Función estática para extraer parámetros de una URL según patrón de ruta
    public static NameValueCollection? ParseUrlParams(string uPath, string rPath)
    {
        string[] uParts = uPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] rParts = rPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (uParts.Length != rParts.Length) return null;

        var parameters = new NameValueCollection();

        for (int i = 0; i < rParts.Length; i++)
        {
            string uPart = uParts[i];
            string rPart = rParts[i];

            if (rPart.StartsWith(":"))
            {
                string paramName = rPart.Substring(1);
                parameters[paramName] = HttpUtility.UrlDecode(uPart);
            }
            else if (uPart != rPart)
            {
                return null; // ruta no coincide
            }
        }
        return parameters;
    }
}