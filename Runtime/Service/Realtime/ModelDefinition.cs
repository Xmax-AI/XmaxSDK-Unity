//
//  ModelDefinition.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The registered server identifier and sizing rules for a supported model.</summary>
    internal sealed class ModelDefinition
    {

        /// <summary>The exact model identifier sent to the session API.</summary>
        internal string Name { get; }

        /// <summary>The immutable media limits and supported generation canvases.</summary>
        internal RealtimeModelCapabilities Capabilities { get; }

        /// <summary>Creates a registered model definition.</summary>
        internal ModelDefinition(
            string name,
            RealtimeModelCapabilities capabilities
        )
        {
            Name = name;
            Capabilities = capabilities;
        }
    }
}
