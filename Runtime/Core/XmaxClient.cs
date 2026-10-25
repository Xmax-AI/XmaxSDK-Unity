//
//  XmaxClient.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Provides shared client configuration and services; creating a client applies process-wide logging preferences.</summary>
    public sealed class XmaxClient
    {

        /// <summary>The general business API used by storage managers.</summary>
        private readonly IApiService _api;

        /// <summary>The realtime session API shared by realtime managers.</summary>
        private readonly IApiService _realtimeApi;

        /// <summary>The immutable configuration used to create this client.</summary>
        public XmaxConfiguration Configuration { get; }

        /// <summary>Creates a client and applies global logging options and detail language. API credentials are validated before online requests.</summary>
        public XmaxClient(
            XmaxConfiguration configuration
        )
        {
            Configuration = configuration ?? throw new XmaxException(
                XmaxErrorCode.InvalidConfiguration,
                "Xmax configuration is required."
            );
            _api = new ApiService(Configuration);
            _realtimeApi = new ApiService(
                Configuration,
                realtime: true
            );
            XmaxLogger.Configure(
                Configuration.LoggerOptions,
                Configuration.LogLanguage
            );
        }

        /// <summary>Creates an independent realtime manager using this client's shared configuration and API service.</summary>
        public XmaxRealtimeManager CreateRealtimeManager(
            RealtimeConfiguration options
        )
        {
            return new XmaxRealtimeManager(
                Configuration,
                options,
                new RealtimeSessionService(_realtimeApi),
                null
            );
        }

        /// <summary>Creates a storage manager using the general API for this client's service environment.</summary>
        ///
        /// <remarks>Create and use the manager on the Unity main thread.</remarks>
        public XmaxStorageManager CreateStorageManager()
        {
            Configuration.Validate();
            return new XmaxStorageManager(new StorageService(
                _api,
                new StorageManager()
            ));
        }

        /// <summary>Creates a service for local model capability and media-size queries.</summary>
        public MediaService CreateMediaService() => new MediaService();
    }
}
