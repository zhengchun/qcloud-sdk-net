using System;

namespace QCloudSDK.COS
{
    /// <summary>
    /// COS密钥配置。
    /// </summary>
    public sealed class Configuration
    {
        /// <summary>
        /// 应用ID。
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// 密钥ID。
        /// </summary>
        public string SecretId { get; set; }

        /// <summary>
        /// 密钥KEY。
        /// </summary>
        public string SecretKey { get; set; }
    }
}
