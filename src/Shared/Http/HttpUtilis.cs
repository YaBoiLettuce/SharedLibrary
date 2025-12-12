namespace Shared.Http;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using System.Xml.Linq;
using Shared.Config;
public static class HttpUtils
{


public static async Task StructuredLogging(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
var requestId = props["req.id"]?.ToString() ??
Guid.NewGuid().ToString("n").Substring(0, 12);
var startUtc = DateTime.UtcNow;
var method = req.HttpMethod ?? "UNKNOWN";
var url = req.Url!.OriginalString ?? req.Url!.ToString();
var remote = req.RemoteEndPoint.ToString() ?? "unknown";
res.Headers["X-Request-Id"] = requestId;
try
{
await next();
}
finally
{
var duration = (DateTime.UtcNow - startUtc).TotalNanoseconds;
var record = new
{
timestamp = startUtc.ToString("o"),
requestId,
method,
url,
remote,
statusCode = res.StatusCode,
contentType = res.ContentType,
contentLength = res.ContentLength64,
duration
};
Console.WriteLine(JsonSerializer.Serialize(record,
JsonSerializerOptions.Web));
}
}

public static async Task CentralizedErrorHandling(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
try
{
await next();
}
catch(Exception e)
{
int code = (int) HttpStatusCode.InternalServerError;
string message =
Environment.GetEnvironmentVariable("DEPLOYMENT_MODE") == "production"
? "An unexpected error occurred." : e.ToString();
await SendResponse(req, res, props, code, message, "text/plain");
}
}

public static async Task DefaultResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
await next();
if(res.StatusCode == HttpRouter.RESPONSE_NOT_SENT)
{
res.StatusCode = (int) HttpStatusCode.NotFound;
res.Close();
}
}

public static async Task ServeStaticFiles(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
string rootDir = Configuration.Get("root.dir",
Directory.GetCurrentDirectory())!;
string urlPath = req.Url!.AbsolutePath.TrimStart('/');
string filePath = Path.Combine(rootDir, urlPath.Replace('/',
Path.DirectorySeparatorChar));
if(File.Exists(filePath))
{
using var fs = File.OpenRead(filePath);
res.StatusCode = (int) HttpStatusCode.OK;
res.ContentType = GetMimeType(filePath);
res.ContentLength64 = fs.Length;
await fs.CopyToAsync(res.OutputStream);
res.Close();
}
await next();
}

private static string GetMimeType(string filePath)
{
string ext = Path.GetExtension(filePath).ToLowerInvariant();
return ext switch
{
".html" => "text/html; charset=utf-8",
".htm" => "text/html; charset=utf-8",
".css" => "text/css",
".js" => "application/javascript",
".json" => "application/json",
".png" => "image/png",
".jpg" => "image/jpeg",
".jpeg" => "image/jpeg",
".gif" => "image/gif",
".svg" => "image/svg+xml",
".ico" => "image/x-icon",
".txt" => "text/plain; charset=utf-8",
_ => "application/octet-stream"
};
}

public static async Task AddResponseCorsHeaders(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
bool isProductionMode = Environment.GetEnvironmentVariable(
"DEPLOYMENT_MODE") == "Production";
string? origin = req.Headers["Origin"];
if(!string.IsNullOrEmpty(origin))
{
if(!isProductionMode)
{
// Allow everything during development
res.AddHeader("Access-Control-Allow-Origin", origin);
res.AddHeader("Access-Control-Allow-Headers",
"Content-Type, Authorization");
res.AddHeader("Access-Control-Allow-Methods",
"GET, POST, PUT, DELETE, OPTIONS");
res.AddHeader("Access-Control-Allow-Credentials", "true");
}
else
{
string[] allowedOrigins = Configuration
.Get("allowed.origins", string.Empty)!
.Split(';', StringSplitOptions.TrimEntries |
StringSplitOptions.RemoveEmptyEntries);
if(allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
{
res.AddHeader("Access-Control-Allow-Origin", origin);
res.AddHeader("Access-Control-Allow-Headers",
"Content-Type, Authorization");
res.AddHeader("Access-Control-Allow-Methods",
"GET, POST, PUT, DELETE, OPTIONS");
res.AddHeader("Access-Control-Allow-Credentials", "true");
}
}
}
if(req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
{
res.StatusCode = (int) HttpStatusCode.NoContent;
res.OutputStream.Close();
}
await next();
}

public static NameValueCollection ParseUrl(string url)
{
int i = -1;
var (scheme, apqf) = (i = url.IndexOf("://")) >= 0
? (url.Substring(0, i), url.Substring(i + 3)) : ("", url);
var (auth, pqf) = (i = apqf.IndexOf("/")) >= 0
? (apqf.Substring(0, i), apqf.Substring(i)) : (apqf, "");
var (up, hp) = (i = auth.IndexOf("@")) >= 0
? (auth.Substring(0, i), auth.Substring(i + 1)) : ("", auth);
var (user, pass) = (i = up.IndexOf(":")) >= 0
? (up.Substring(0, i), up.Substring(i + 1)) : (up, "");
var (host, port) = (i = hp.IndexOf(":")) >= 0
? (hp.Substring(0, i), hp.Substring(i + 1)) : (hp, "");
var (pq, fragment) = (i = pqf.IndexOf("#")) >= 0
? (pqf.Substring(0, i), pqf.Substring(i + 1)) : (pqf, "");
var (path, query) = (i = pq.IndexOf("?")) >= 0
? (pq.Substring(0, i), pq.Substring(i + 1)) : (pq, "");
var parts = new NameValueCollection();
// https://john:abc123@site.com:8080/api/v1/users/3?q=0&active=true#bio
// scheme://user:pass@host:port/path?query#fragment
// Splits:1 4 3 5 2 7 6
// 1 scheme user:pass@host:port/path?query#fragment
// 2 user:pass@host:port (auth) /path?query#fragment
// 3 user:pass host:port
// 4 user pass
// 5 host port
// 6 /path?query fragment
// 7 /path query
parts["scheme"] = scheme; // https
parts["auth"] = auth; // john:abc123@site.com:8080
parts["user"] = user; // john
parts["pass"] = pass; // abc123
parts["host"] = host; // site.com
parts["port"] = port; // 8080
parts["path"] = path; // /api/vi/users/3
parts["query"] = query; // q=0&active=true
parts["fragment"] = fragment; // bio
return parts;
}

public static async Task ParseRequestUrl(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
props["req.url"] = ParseUrl(req.Url!.OriginalString);
await next();
}

public static NameValueCollection ParseQueryString(string text,
string duplicateSeparator = ",")
{
if(text.StartsWith('?')) { text = text.Substring(1); }
return ParseFormData(text, duplicateSeparator);
}

public static async Task ParseRequestQueryString(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
var url = (NameValueCollection?) props["req.url"];
props["req.query"] = ParseQueryString(url?["query"] ?? req.Url!.Query);
await next();
}

public static NameValueCollection ParseFormData(string text,
string duplicateSeparator = ",")
{
var result = new NameValueCollection();
var pairs = text.Split('&', StringSplitOptions.RemoveEmptyEntries);
foreach(var pair in pairs)
{
var kv = pair.Split('=', 2, StringSplitOptions.None);
var key = HttpUtility.UrlDecode(kv[0]);
var value = kv.Length > 1 ? HttpUtility.UrlDecode(kv[1]) : string.Empty;
var oldValue = result[key];
result[key] = oldValue == null
? value : oldValue + duplicateSeparator + value;
}
return result;
}

public static async Task ReadRequestBodyAsForm(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
using StreamReader sr = new StreamReader(req.InputStream, Encoding.UTF8);
string formData = await sr.ReadToEndAsync();
props["req.form"] = ParseFormData(formData);
await next();
}

public static async Task ReadRequestBodyAsBlob(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
using var ms = new MemoryStream();
await req.InputStream.CopyToAsync(ms);
props["req.blob"] = ms.ToArray();
await next();
}

public static async Task ReadRequestBodyAsText(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
var encoding = req.ContentEncoding ?? Encoding.UTF8;
using StreamReader sr = new StreamReader(req.InputStream, encoding);
props["req.text"] = await sr.ReadToEndAsync();
await next();
}

public static async Task ReadRequestBodyAsJson(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
props["req.json"] = (await JsonNode.ParseAsync(req.InputStream))!.AsObject();
await next();
}

public static async Task ReadRequestBodyAsXml(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Func<Task> next)
{
props["req.xml"] = await XDocument.LoadAsync(req.InputStream,
LoadOptions.None, CancellationToken.None);
await next();
}

public static string DetectContentType(string text)
{
string s = text.TrimStart();
if(s.StartsWith("{") || s.StartsWith("["))
{
return "application/json";
}
else if(s.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
s.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
{
return "text/html";
}
else if(s.StartsWith("<", StringComparison.Ordinal))
{
return "application/xml";
}
else
{
return "text/plain";
}
}

public static async Task SendOkResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props)
{
await SendOkResponse(req, res, props, string.Empty, "text/plain");
}
public static async Task SendOkResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, string content)
{
await SendOkResponse(req, res, props, content, DetectContentType(content));
}
public static async Task SendOkResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props,
string content, string contentType)
{
await SendResponse(req, res, props, (int) HttpStatusCode.OK,
content, contentType);
}

public static async Task SendNotFoundResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props)
{
await SendNotFoundResponse(req, res, props, string.Empty, "text/plain");
}
public static async Task SendNotFoundResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, string content)
{
await SendNotFoundResponse(req, res, props, content,
DetectContentType(content));
}
public static async Task SendNotFoundResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props,
string content, string contentType)
{
await SendResponse(req, res, props, (int) HttpStatusCode.NotFound,
content, contentType);
}

public static async Task SendResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, int statusCode, string content)
{
await SendResponse(req, res, props, statusCode, content,
DetectContentType(content));
}
public static async Task SendResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, int statusCode,
string content, string contentType)
{
await SendResponse(req, res, props, statusCode,
Encoding.UTF8.GetBytes(content), DetectContentType(content));
}
public static async Task SendResponse(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, int statusCode,
byte[] content, string contentType)
{
res.StatusCode = statusCode;
res.ContentEncoding = Encoding.UTF8;
res.ContentType = contentType;
res.ContentLength64 = content.LongLength;
await res.OutputStream.WriteAsync(content);
res.Close();
}

namespace Shared.Http;
using System.Net;
public class Result<T>
{
public bool IsError { get; }
public Exception? Error { get; }
public T? Payload { get; }
public int StatusCode { get; }
public Result(Exception error, int statusCode =
(int) HttpStatusCode.InternalServerError)
{
IsError = true;
Error = error;
Payload = default(T);
StatusCode = statusCode;
}
public Result(T payload, int statusCode = (int) HttpStatusCode.OK)
{
IsError = false;
Error = null;
Payload = payload;
StatusCode = statusCode;
}
}

public static async Task SendResultResponse<T>(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props, Result<T> result)
{
if(result.IsError)
{
res.Headers["Cache-Control"] = "no-store";
await HttpUtils.SendResponse(req, res, props, result.StatusCode,
result.Error!.ToString()!);
}
else
{
await HttpUtils.SendResponse(req, res, props, result.StatusCode,
result.Payload!.ToString()!);
}
}
}

namespace Shared.Http;
public class PagedResult<T>
{
public int TotalCount { get; }
public List<T> Values { get; }
public PagedResult(int totalCount, List<T> values)
{
TotalCount = totalCount;
Values = values;
}
}

public static async Task SendPagedResultResponse<T>(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props,
Result<PagedResult<T>> result, int page, int size)
{
if(result.IsError)
{
res.Headers["Cache-Control"] = "no-store";
await HttpUtils.SendResponse(req, res, props, result.StatusCode,
result.Error!.ToString()!);
}
else
{
var pagedResult = result.Payload!;
HttpUtils.AddPaginationHeaders(req, res, props, pagedResult, page, size);
await HttpUtils.SendResponse(req, res, props, result.StatusCode,
result.Payload!.ToString()!);
}
}

public static void AddPaginationHeaders<T>(HttpListenerRequest req,
HttpListenerResponse res, Hashtable props,
PagedResult<T> pagedResult, int page, int size)
{
var baseUrl =
$"{req.Url!.Scheme}://{req.Url!.Authority}{req.Url!.AbsolutePath}";
int totalPages =
Math.Max(1, (int) Math.Ceiling((double) pagedResult.TotalCount / size));
string self =
$"{baseUrl}?page={page}&size={size}";
string? first =
page == 1 ? null : $"{baseUrl}?page={1}&size={size}";
string? last =
page == totalPages ? null : $"{baseUrl}?page={totalPages}&size={size}";
string? prev =
page > 1 ? $"{baseUrl}?page={page - 1}&size={size}" : null;
string? next =
page < totalPages ? $"{baseUrl}?page={page + 1}&size={size}" : null;
res.Headers["Content-Type"] = "application/json; charset=utf-8";
res.Headers["X-Total-Count"] = pagedResult.TotalCount.ToString();
res.Headers["X-Page"] = page.ToString();
res.Headers["X-Page-Size"] = size.ToString();
res.Headers["X-Total-Pages"] = totalPages.ToString();
// Optional RFC 5988 Link header for discoverability
var linkParts = new List<string>();
if(prev != null) { linkParts.Add($"<{prev}>; rel=\"prev\""); }
if(next != null) { linkParts.Add($"<{next}>; rel=\"next\""); }
if(first != null) { linkParts.Add($"<{first}>; rel=\"first\""); }
if(last != null) { linkParts.Add($"<{last}>; rel=\"last\""); }
if(linkParts.Count > 0)
{
res.Headers["Link"] = string.Join(", ", linkParts);
}
}
