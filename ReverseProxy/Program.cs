using System.IO.Compression;
using System.Text;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile($"appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: true);

// 原始碼出處：https://blog.darkthread.net/blog/yarp-lab/
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        context.AddResponseTransform(async respCtx =>
        {
            var resp = respCtx.HttpContext.Response;
            var req = respCtx.HttpContext.Request;
            var srcResp = respCtx.ProxyResponse;
            if ((srcResp?.Content.Headers.ContentType?.MediaType ?? string.Empty).Contains("html"))
            {
                var stream = await srcResp?.Content.ReadAsStreamAsync()!;
                // Decompress the response stream if needed
                if (srcResp.Content.Headers.ContentEncoding.Any())
                {
                    resp.Headers.Remove("Content-Encoding");
                    stream = new GZipStream(stream, CompressionMode.Decompress);
                }
                using var reader = new StreamReader(stream);
                // TODO: size limits, timeouts
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrEmpty(body))
                {
                    respCtx.SuppressResponseBody = true;
                    // 停用廣告相關 JavaScript
                    body = body.Replace("src=\"//pagead2", "_=\"");
                    body = body.Replace("src=\"https://pagead2", "_=\"");
                    body = body.Replace("src=\"https://player.gliacloud.com", "_=\"");
                    // 將圖片連結轉換為本地代理、移除廣告區塊
                    body = body.Replace("</body>", @"<script>
document.querySelectorAll('img[data-src]').forEach(img => {
    let src = img.getAttribute('data-src'); // 原程式採 Lazy-Loading，檢視到才下載，URL 在 data-src
    img.setAttribute('data-src', src.replace(/https:\/\/([^.]+).read01.com/, '/imgbed-$1'));
});
document.querySelectorAll('.axslot').forEach(o => o.remove());
</script></body>");
                    var bytes = Encoding.UTF8.GetBytes(body);
                    resp.ContentLength = bytes.Length;
                    await respCtx.HttpContext.Response.Body.WriteAsync(bytes);
                }
            }
        });
    });

var app = builder.Build();

app.UseCors(builder =>
{
    builder.WithOrigins("http://localhost:7266");
});
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors 'self' https://localhost:7266");
    await next();
});

// 原始碼出處：https://blog.darkthread.net/blog/yarp-lab/
// 處理圖床照片下載
app.MapGet("/imgbed-{hostName}/{**path}", async (HttpContext ctx, string hostName, string path) =>
{
    var url = $"https://{hostName}.read01.com/{path}";
    var client = new HttpClient();
    var resp = await client.GetAsync(url);
    if (resp.IsSuccessStatusCode)
    {
        var contentType = resp.Content.Headers.ContentType!.ToString();
        ctx.Response.ContentType = contentType;
        await resp.Content.CopyToAsync(ctx.Response.Body);
    }
    else
    {
        ctx.Response.StatusCode = (int)resp.StatusCode;
    }
});
app.MapReverseProxy();
app.Run();
