//
//  XmaxCreditBalance.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The server-reported credit balance for the account owning the client's API key.</summary>
    public readonly struct XmaxCreditBalance
    {

        /// <summary>The remaining credits as a finite, nonnegative value, including fractional credits.</summary>
        public double CurrentBalance { get; }

        /// <summary>Creates a balance after the API payload has been validated.</summary>
        internal XmaxCreditBalance(
            double currentBalance
        )
        {
            CurrentBalance = currentBalance;
        }
    }
}
