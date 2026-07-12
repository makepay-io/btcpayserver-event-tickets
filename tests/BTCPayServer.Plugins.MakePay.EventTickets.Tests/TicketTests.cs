using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using Xunit;
namespace BTCPayServer.Plugins.MakePay.EventTickets.Tests;
public class TicketTests
{
    [Fact] public void QrPayloadRoundTripsCode() { var code="TKT-AAAA-BBBB-CCCC-DDDD"; Assert.Equal(code,TicketCodeService.ExtractCode(TicketCodeService.QrPayload("store",code))); }
    [Fact] public void CodeHashNormalizesCaseAndSpaces() { Assert.Equal(TicketCodeService.Hash(" tkt-ab "),TicketCodeService.Hash("TKT-AB")); }
    [Fact] public void ForeignQrPrefixFallsBackToRawValue() { Assert.Equal("hello",TicketCodeService.ExtractCode("hello")); }
}
