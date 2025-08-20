namespace eShop.CryptoPayment.API.Services;

public interface IIdentityService
{
    string GetUserIdentity();
    string GetUserName();
}

public class IdentityService : IIdentityService
{
    private readonly IHttpContextAccessor _context;

    public IdentityService(IHttpContextAccessor context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public string GetUserIdentity()
    {
        return _context.HttpContext?.User?.FindFirst("sub")?.Value ?? string.Empty;
    }

    public string GetUserName()
    {
        return _context.HttpContext?.User?.Identity?.Name ?? string.Empty;
    }
}