using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace CryptoPayment.BlockchainServices.Security;

public interface ISecureDisposalService
{
    void SecureDispose(string sensitiveData);
    void SecureDispose(byte[] sensitiveData);
    void SecureDispose(SecureString sensitiveData);
    void SecureDispose(char[] sensitiveData);
    void SecureDispose(IntPtr sensitivePointer, int length);
    void ScheduleGarbageCollection();
}

public class SecureDisposalService : ISecureDisposalService
{
    private readonly ILogger<SecureDisposalService> _logger;

    public SecureDisposalService(ILogger<SecureDisposalService> logger)
    {
        _logger = logger;
    }

    public void SecureDispose(string sensitiveData)
    {
        if (string.IsNullOrEmpty(sensitiveData))
            return;

        try
        {
            // Convert string to char array for secure disposal
            var chars = sensitiveData.ToCharArray();
            SecureDispose(chars);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to securely dispose string data");
        }
    }

    public void SecureDispose(byte[] sensitiveData)
    {
        if (sensitiveData == null || sensitiveData.Length == 0)
            return;

        try
        {
            // Overwrite with random data first
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(sensitiveData);
            
            // Then clear with zeros
            Array.Clear(sensitiveData, 0, sensitiveData.Length);
            
            // Additional overwrite with different pattern
            Array.Fill(sensitiveData, (byte)0xFF);
            Array.Clear(sensitiveData, 0, sensitiveData.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to securely dispose byte array data");
        }
    }

    public void SecureDispose(SecureString sensitiveData)
    {
        if (sensitiveData == null)
            return;

        try
        {
            sensitiveData.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to securely dispose SecureString data");
        }
    }

    public void SecureDispose(char[] sensitiveData)
    {
        if (sensitiveData == null || sensitiveData.Length == 0)
            return;

        try
        {
            // Overwrite with random characters
            var random = new Random();
            for (int i = 0; i < sensitiveData.Length; i++)
            {
                sensitiveData[i] = (char)random.Next(32, 127);
            }
            
            // Clear with null characters
            Array.Clear(sensitiveData, 0, sensitiveData.Length);
            
            // Additional overwrite with different pattern
            Array.Fill(sensitiveData, 'X');
            Array.Clear(sensitiveData, 0, sensitiveData.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to securely dispose char array data");
        }
    }

    public void SecureDispose(IntPtr sensitivePointer, int length)
    {
        if (sensitivePointer == IntPtr.Zero || length <= 0)
            return;

        try
        {
            // Zero out the memory
            unsafe
            {
                byte* ptr = (byte*)sensitivePointer.ToPointer();
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = 0;
                }
            }

            // Use Windows API to zero memory if available
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeMethods.SecureZeroMemory(sensitivePointer, (UIntPtr)length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to securely dispose pointer memory");
        }
    }

    public void ScheduleGarbageCollection()
    {
        try
        {
            // Force garbage collection to ensure disposed objects are cleaned up
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to schedule garbage collection");
        }
    }
}

// Native methods for secure memory operations
internal static class NativeMethods
{
    [DllImport("kernel32.dll", EntryPoint = "RtlSecureZeroMemory")]
    internal static extern void SecureZeroMemory(IntPtr ptr, UIntPtr cnt);
}

public static class SecureDisposalExtensions
{
    public static IServiceCollection AddSecureDisposal(this IServiceCollection services)
    {
        services.AddSingleton<ISecureDisposalService, SecureDisposalService>();
        return services;
    }

    public static void SecureDispose(this string sensitiveData, ISecureDisposalService disposalService)
    {
        disposalService.SecureDispose(sensitiveData);
    }

    public static void SecureDispose(this byte[] sensitiveData, ISecureDisposalService disposalService)
    {
        disposalService.SecureDispose(sensitiveData);
    }

    public static void SecureDispose(this char[] sensitiveData, ISecureDisposalService disposalService)
    {
        disposalService.SecureDispose(sensitiveData);
    }
}

// Secure disposal helper for automatic cleanup
public class SecureDisposable<T> : IDisposable where T : class
{
    private readonly T _value;
    private readonly ISecureDisposalService _disposalService;
    private bool _disposed = false;

    public SecureDisposable(T value, ISecureDisposalService disposalService)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _disposalService = disposalService ?? throw new ArgumentNullException(nameof(disposalService));
    }

    public T Value
    {
        get
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SecureDisposable<T>));
            return _value;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            if (_value is string str)
                _disposalService.SecureDispose(str);
            else if (_value is byte[] bytes)
                _disposalService.SecureDispose(bytes);
            else if (_value is char[] chars)
                _disposalService.SecureDispose(chars);
            else if (_value is SecureString secureStr)
                _disposalService.SecureDispose(secureStr);

            _disposed = true;
        }
    }

    ~SecureDisposable()
    {
        Dispose(false);
    }
}