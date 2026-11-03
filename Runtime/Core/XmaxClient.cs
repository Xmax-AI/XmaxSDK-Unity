//
//  XmaxClient.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Provides account queries and shared client services; creating a client applies process-wide logging preferences.</summary>
    public sealed class XmaxClient
    {

        /// <summary>The general business API used by account queries and storage managers.</summary>
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

        /// <summary>Fetches remaining credits for the account owning this client's API key.</summary>
        ///
        /// <remarks>
        /// Call on the Unity main thread. Each call reads the current server balance from
        /// the configured general API environment without creating a realtime session.
        /// Fractional credits are preserved; invalid balances fail instead of returning zero.
        /// </remarks>
        ///
        /// <param name="cancellationToken">Cancels the query and its HTTP request.</param>
        ///
        /// <returns>The server-reported remaining credits.</returns>
        ///
        /// <exception cref="XmaxException">Credentials, transport, or the response are invalid.</exception>
        /// <exception cref="System.OperationCanceledException">The query is cancelled.</exception>
        public async Task<XmaxCreditBalance> GetCreditBalanceAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Configuration.Validate();
            var response = await _api.RequestAsync(
                "GET",
                "/balance",
                null,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();

            var value = response != null && response.IsObject && response.ContainsKey("currentBalance")
                ? response["currentBalance"] : null;
            var currentBalance = value != null && value.IsNumber ? (double)value : double.NaN;
            if (double.IsNaN(currentBalance) || double.IsInfinity(currentBalance) || currentBalance < 0)
            {
                throw new XmaxException(
                    XmaxErrorCode.ApiError,
                    "Credit balance must be a finite, nonnegative number."
                );
            }

            return new XmaxCreditBalance(currentBalance);
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
