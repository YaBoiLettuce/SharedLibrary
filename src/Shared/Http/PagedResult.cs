using System.Collections;
using System.Net;

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

    public static async Task SendPagedResultResponse<T>(HttpListenerRequest req, HttpListenerResponse res, Hashtable props, Result<PagedResult<T>> result, int page, int size)
    {
        if (result.IsError)
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
}