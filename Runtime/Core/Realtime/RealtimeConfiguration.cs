//
//  RealtimeConfiguration.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The model and adaptive transport settings for a realtime manager.</summary>
    public sealed class RealtimeConfiguration
    {

        /// <summary>The supported realtime generation model.</summary>
        public RealtimeModel Model { get; }

        /// <summary>Whether uplink and downlink adapt automatically; disabled connections start at L1.</summary>
        public bool IsAdaptiveQualityEnabled { get; }

        /// <summary>The registered server name and sizing rules for this model.</summary>
        internal ModelDefinition Definition => RealtimeModelRegistry.Get(Model);

        /// <summary>Creates realtime settings for a supported model.</summary>
        public RealtimeConfiguration(
            RealtimeModel model,
            bool isAdaptiveQualityEnabled = true
        )
        {
            RealtimeModelRegistry.Get(model);
            Model = model;
            IsAdaptiveQualityEnabled = isAdaptiveQualityEnabled;
        }
    }
}
