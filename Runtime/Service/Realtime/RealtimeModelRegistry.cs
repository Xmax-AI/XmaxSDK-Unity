//
//  RealtimeModelRegistry.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;

namespace Xmax.SDK
{

    /// <summary>
    /// Registers each built-in server model and its immutable capabilities once.
    /// </summary>
    internal static class RealtimeModelRegistry
    {

        /// <summary>
        /// The model definitions indexed by public model identity.
        /// </summary>
        private static readonly IReadOnlyDictionary<RealtimeModel, ModelDefinition> Definitions =
            new Dictionary<RealtimeModel, ModelDefinition>
            {
                [RealtimeModel.X2_1] = CreateX21Definition()
            };

        /// <summary>
        /// Creates the X2.1 server identity and sizing capabilities.
        /// </summary>
        private static ModelDefinition CreateX21Definition()
        {
            return new ModelDefinition(
                "x2.1",
                new RealtimeModelCapabilities(
                    200000,
                    2100000,
                    32,
                    new RealtimeVideoFormat(
                        1080,
                        1920,
                        30
                    ),
                    new RealtimeVideoSize(
                        1920,
                        1080
                    ),
                    new RealtimeVideoSize(
                        1080,
                        1920
                    ),
                    new RealtimeVideoSize(
                        1408,
                        1408
                    ),
                    new RealtimeVideoSize(
                        1152,
                        1536
                    ),
                    new RealtimeVideoSize(
                        1536,
                        1152
                    )
                )
            );
        }

        /// <summary>
        /// Returns a registered model definition or rejects an unknown model identity.
        /// </summary>
        internal static ModelDefinition Get(
            RealtimeModel model
        )
        {
            if (Definitions.TryGetValue(
                model,
                out var definition
            ))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(model),
                model,
                "Realtime model is not registered."
            );
        }

    }
}
