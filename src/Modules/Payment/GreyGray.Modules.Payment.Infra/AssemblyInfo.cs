using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GreyGray.M1a.PaymentLedger.Tests")]
[assembly: InternalsVisibleTo("GreyGray.M1a.Migrations.Tests")]

// dev 用的綠界模擬器（ADR-029）。它要扮演綠界伺服器，就得用同一套 CheckMacValue 演算法；
// 讓它重用 EcpayGateway.ComputeCheckMacValue 而不是自己抄一份——兩份會漂移，
// 而官方向量（EcpayGatewayTests）只釘得住其中一份。
[assembly: InternalsVisibleTo("GreyGray.Tools.EcpaySimulator.Core")]
