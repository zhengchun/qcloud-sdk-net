using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.XPath;

namespace QCloud.COS
{
    /// <summary>
    /// COS客户端，执行COS的请求。
    /// </summary>
    public sealed class Client
    {
        private readonly HttpClient _backChannel;
        private readonly AppSettings _conf;

        private Client() { }

        /// <summary>
        /// 初始化新的<see cref="Client"/>实例。
        /// </summary>
        /// <param name="conf">密钥配置</param>
        /// <param name="backChannel">自定义<see cref="HttpClient"/>实例。</param>
        public Client(AppSettings conf, HttpClient backChannel = null)
        {
            _conf = conf;
            _backChannel = backChannel ?? new HttpClient();
        }

        /// <summary>
        /// ListBucketsAsync返回所有存储空间列表。
        /// </summary>
        /// <returns></returns>
        public async Task<Bucket[]> ListBucketsAsync()
        {
            const string endpoint = "https://service.cos.myqcloud.com/";
            var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using (var resp = await SendAsync(req))
            {
                if (resp.StatusCode != HttpStatusCode.OK)
                {
                    RequestFailure(HttpMethod.Get, resp.StatusCode, await resp.Content.ReadAsStringAsync());
                }

                var doc = new XmlDocument();
                doc.LoadXml(await resp.Content.ReadAsStringAsync());
                var buckets = new List<Bucket>();
                foreach (XPathNavigator elem in doc.DocumentElement.CreateNavigator().Select("//Buckets/Bucket"))
                {
                    var fullname = elem.SelectSingleNode("Name").InnerXml;
                    var i = fullname.LastIndexOf("-");
                    var bucket = new Bucket(
                        appId: fullname.Substring(i + 1),
                        name: fullname.Substring(0, i),
                        region: elem.SelectSingleNode("Location").InnerXml);
                    buckets.Add(bucket);
                }
                return buckets.ToArray();
            }
        }

        /// <summary>
        /// PutBucketAsync创建一个新的存储桶(Bucket)。
        /// https://cloud.tencent.com/document/product/436/7738
        /// </summary>
        /// <param name="name">桶名称</param>
        /// <param name="region">桶在区域</param>
        /// <param name="header">自定义附加请求的标头</param>
        /// <returns></returns>
        public async Task<Bucket> PutBucketAsync(string name, string region, Dictionary<string, string> headers = null)
        {
            var bucket = new Bucket(_conf.AppId, name, region);
            var endpoint = bucket.Url + "/";
            var req = new HttpRequestMessage(HttpMethod.Put, endpoint);
            if (headers?.Count > 0)
            {
                foreach (var header in headers)
                {
                    req.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            using (var resp = await SendAsync(req))
            {
                var payload = await resp.Content.ReadAsStringAsync();
                if (resp.StatusCode != HttpStatusCode.OK)
                {
                    RequestFailure(HttpMethod.Put, resp.StatusCode, await resp.Content.ReadAsStringAsync());
                }
                return bucket;
            }
        }

        /// <summary>
        /// DeleteBucketAsync删除一个指定的存储桶(Bucket)。
        /// </summary>
        /// <param name="name">桶名称</param>
        /// <param name="region">桶在区域</param>
        /// <returns></returns>
        public async Task DeleteBucketAsync(string name, string region)
        {
            var bucket = new Bucket(_conf.AppId, name, region);
            var endpoint = bucket.Url + "/";
            var req = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            using (var resp = await SendAsync(req))
            {
                switch (resp.StatusCode)
                {
                    case HttpStatusCode.OK:
                    case HttpStatusCode.NoContent:
                        break;
                    default:
                        RequestFailure(HttpMethod.Delete, resp.StatusCode, await resp.Content.ReadAsStringAsync());
                        break;
                }
            }
        }

        /// <summary>
        /// PutObjectAsync上传文件到指定的URL。
        /// https://cloud.tencent.com/document/product/436/7749
        /// </summary>
        /// <param name="url"></param>
        /// <param name="content"></param>
        /// <param name="header"></param>
        /// <returns></returns>
        public async Task PutObjectAsync(string url, Stream content, Dictionary<string, string> headers = null)
        {
            if (content == null)
            {
                throw new ArgumentNullException("content");
            }
            var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StreamContent(content)
            };
            if (headers?.Count > 0)
            {
                foreach (var header in headers)
                {
                    req.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            using (var resp = await SendAsync(req))
            {
                if (resp.StatusCode != HttpStatusCode.OK)
                {
                    RequestFailure(HttpMethod.Put, resp.StatusCode, await resp.Content.ReadAsStringAsync());
                }
            }
        }

        /// <summary>
        /// GetObjectAsync读取上传的文件内容。
        /// https://cloud.tencent.com/document/product/436/7753
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<Stream> GetObjectAsync(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await SendAsync(req);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                RequestFailure(HttpMethod.Put, resp.StatusCode, await resp.Content.ReadAsStringAsync());
            }
            return await resp.Content.ReadAsStreamAsync();
        }

        /// <summary>
        /// DeleteObjectAsync删除指定的文件。
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task DeleteObjectAsync(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            using (var resp = await SendAsync(req))
            {
                switch (resp.StatusCode)
                {
                    case HttpStatusCode.OK:
                    case HttpStatusCode.NoContent:
                        break;
                    default:
                        RequestFailure(HttpMethod.Put, resp.StatusCode, await resp.Content.ReadAsStringAsync());
                        break;
                }                
            }
        }

        private void RequestFailure(HttpMethod method, HttpStatusCode respStatusCode, string respContent)
        {
            var doc = new XmlDocument();
            doc.LoadXml(respContent);
            var root = doc.DocumentElement.CreateNavigator().SelectSingleNode("//Error");
            var ex = new RequestFailureException(method.ToString(), root.SelectSingleNode("Message").InnerXml)
            {
                HttpStatusCode = (int)respStatusCode,
                ErrorCode = root.SelectSingleNode("Code").InnerXml,
                ResourceURL = root.SelectSingleNode("Resource").InnerXml,
                RequestId = root.SelectSingleNode("RequestId").InnerXml,
                TraceId = root.SelectSingleNode("TraceId").InnerXml,
            };
            throw ex;
        }

        private static string HashHMACSHA1(string key, string content)
        {
            var buff = new HMACSHA1(Encoding.UTF8.GetBytes(key)).ComputeHash(Encoding.UTF8.GetBytes(content));
            return string.Concat(buff.Select(k => k.ToString("x2")));
        }

        private static string HashSHA1(string content)
        {
            var buff = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(content));
            return string.Concat(buff.Select(k => k.ToString("x2")));
        }

        private string SignRequest(HttpRequestMessage req)
        {
            var qs = HttpUtility.ParseQueryString(req.RequestUri.Query);
            var sortedQuerys = qs.Cast<string>()
                .Select(k => new KeyValuePair<string, string>(k.ToLower(), qs[k].ToLower()))
                .OrderBy(k => k.Key);
            var sortedHeaders = req.Headers.Select(k => new KeyValuePair<string, string>(k.Key.ToLower(), Uri.EscapeDataString(k.Value.First()).ToLower()))
                .OrderBy(k => k.Key);
            var reqPayload = $"{req.Method.ToString().ToLower()}\n" +
                $"{req.RequestUri.LocalPath}\n" +
                $"{string.Join("&", sortedQuerys.Select(k => k.Key + "=" + k.Value))}\n" +
                $"{string.Join("&", sortedHeaders.Select(k => k.Key + "=" + k.Value))}\n";
            // Sign
            var now = DateTimeOffset.Now;
            var signTime = $"{now.ToUnixTimeSeconds()};{now.AddSeconds(30).ToUnixTimeSeconds()}";
            var signKey = HashHMACSHA1(_conf.SecretKey, signTime);
            var payloadStr = $"sha1\n{signTime}\n{HashSHA1(reqPayload)}\n";
            var signature = HashHMACSHA1(signKey, payloadStr);
            var m = new Dictionary<string, string>()
            {
                { "q-sign-algorithm", "sha1"},
                { "q-ak",             _conf.SecretId },
                { "q-sign-time",      signTime },
                { "q-key-time",       signTime },
                { "q-header-list",  string.Join(";",sortedHeaders.Select(k=>k.Key)) },
                { "q-url-param-list", string.Join(";",sortedQuerys.Select(k=>k.Key)) },
                { "q-signature",     signature }
            };
            return string.Join("&", m.Select(k => k.Key + "=" + k.Value));
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req)
        {
            req.Headers.Host = req.RequestUri.Host;
            req.Headers.TryAddWithoutValidation("Authorization", SignRequest(req));
            return await _backChannel.SendAsync(req);
        }
    }
}
