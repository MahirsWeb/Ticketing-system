using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TicketSystemTech.Api.Configuration;

/// <summary>
/// The Data Protection key ring holds the keys that encrypt the personal data and the mailbox tokens. It lives in the same
/// database as the data it protects, so a copy of the database was a copy of the locks and the keys together. With a
/// certificate configured, new keys are written to the database encrypted with it, and the certificate (which is NOT in the
/// database or its backups) is needed to read them.
///
/// Settings (section <c>DataProtection</c>): <c>CertificatePath</c> + <c>CertificatePassword</c> for a .pfx file, or
/// <c>CertificateThumbprint</c> (+ <c>CertificateStoreLocation</c>, CurrentUser or LocalMachine) for the Windows certificate store.
/// </summary>
public static class KeyRingProtection
{
    /// <summary>True when a certificate is named at all (whether it can be loaded is another question).</summary>
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"])
        || !string.IsNullOrWhiteSpace(configuration["DataProtection:CertificateThumbprint"]);

    /// <summary>The configured certificate, or null when none is configured. Throws with a plain message when one is configured
    /// and cannot be used: a key ring that quietly stays unprotected would be worse than a refusal to start.</summary>
    public static X509Certificate2? Load(IConfiguration configuration, string contentRootPath)
    {
        var path = configuration["DataProtection:CertificatePath"];
        var thumbprint = configuration["DataProtection:CertificateThumbprint"];
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(thumbprint)) return null;

        X509Certificate2 certificate;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var full = Path.GetFullPath(path, contentRootPath);
            if (!File.Exists(full))
                throw new InvalidOperationException($"DataProtection:CertificatePath points to '{full}', which does not exist.");
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12FromFile(full, configuration["DataProtection:CertificatePassword"],
                    X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            }
            catch (CryptographicException ex)
            {
                // The usual cause is a wrong password; the message of the exception is about the file format, so say it in words.
                throw new InvalidOperationException(
                    $"The certificate file '{full}' could not be opened: the file is not a valid .pfx or DataProtection:CertificatePassword is wrong.", ex);
            }
        }
        else
        {
            var location = string.Equals(configuration["DataProtection:CertificateStoreLocation"], "LocalMachine", StringComparison.OrdinalIgnoreCase)
                ? StoreLocation.LocalMachine
                : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint!.Replace(" ", ""), validOnly: false);
            if (found.Count == 0)
                throw new InvalidOperationException($"No certificate with thumbprint '{thumbprint}' was found in the {location} store.");
            certificate = found[0];
        }

        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The key ring certificate has no private key, so it could encrypt keys but never read them back.");
        return certificate;
    }

    /// <summary>
    /// Writes a new self-signed certificate (RSA 3072, valid 10 years) as a password-protected .pfx. It is only a container for
    /// a key: nobody checks its name or its issuer. Keep the file and its password OUTSIDE the database backups, and back
    /// them up separately: without them the encrypted keys, and with them the personal data, cannot be read.
    /// </summary>
    public static void CreateSelfSigned(string path, string password)
    {
        if (File.Exists(path)) throw new InvalidOperationException($"'{path}' already exists; refusing to overwrite a key ring certificate.");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is required.", nameof(password));

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=TicketSystemTech key ring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(TimeProvider.System.GetUtcNow().AddMinutes(-5), TimeProvider.System.GetUtcNow().AddYears(10));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }

    /// <summary>
    /// The command <c>--create-key-certificate &lt;file.pfx&gt; &lt;password&gt;</c>; returns the exit code (0 made, 1 refused, 2 wrong use).
    /// A mistake of the person at the keyboard (a file that is already there, a path that cannot be written) is an answer on the error
    /// stream, not an unhandled exception with a stack trace and a crash code: the method above keeps throwing, so that no other code can miss it.
    /// </summary>
    public static int RunCreateCommand(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3)
        {
            error.WriteLine("Usage: --create-key-certificate <file.pfx> <password>");
            return 2;
        }

        try
        {
            CreateSelfSigned(args[1], args[2]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        output.WriteLine($"Created {args[1]}. Set DataProtection:CertificatePath and DataProtection:CertificatePassword, and keep the file and the password OUT of the database backups.");
        return 0;
    }
}
