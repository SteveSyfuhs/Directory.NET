using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Directory.Security.CertificateAuthority;

/// <summary>
/// Hosted service that performs certificate auto-enrollment on DC startup.
/// Checks templates with auto-enrollment enabled and issues certificates if needed.
/// </summary>
public class CertificateAutoEnrollmentService : IHostedService, IDisposable
{
    private readonly CertificateAuthorityService _caService;
    private readonly CertificateTemplateService _templateService;
    private readonly ILogger<CertificateAutoEnrollmentService> _logger;
    private Timer _timer;

    public CertificateAutoEnrollmentService(
        CertificateAuthorityService caService,
        CertificateTemplateService templateService,
        ILogger<CertificateAutoEnrollmentService> logger)
    {
        _caService = caService;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Certificate Auto-Enrollment service starting");

        // Try to load existing CA state
        await _caService.TryLoadAsync(cancellationToken);

        // Schedule periodic auto-enrollment check (first run after 30 seconds, then every hour)
        _timer = new Timer(DoAutoEnrollment, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Certificate Auto-Enrollment service stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private async void DoAutoEnrollment(object state)
    {
        try
        {
            if (!_caService.IsInitialized)
            {
                _logger.LogDebug("CA not initialized, skipping auto-enrollment check");
                return;
            }

            var autoEnrollTemplates = await _templateService.GetAutoEnrollTemplatesAsync();
            if (autoEnrollTemplates.Count == 0)
            {
                _logger.LogDebug("No auto-enrollment templates configured");
                return;
            }

            var issuedCerts = await _caService.ListIssuedCertificatesAsync();

            foreach (var template in autoEnrollTemplates)
            {
                // Check if this DC already has a valid certificate from this template
                var hostname = Environment.MachineName;
                var existing = issuedCerts.FirstOrDefault(c =>
                    c.TemplateName == template.Name &&
                    c.Status == "Active" &&
                    c.NotAfter > DateTimeOffset.UtcNow.AddDays(template.RenewalPeriodDays) &&
                    c.Subject.Contains(hostname, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    _logger.LogDebug("Valid certificate exists for template {Template}, skipping", template.Name);
                    continue;
                }

                // Check if there's a certificate that needs renewal
                var expiring = issuedCerts.FirstOrDefault(c =>
                    c.TemplateName == template.Name &&
                    c.Status == "Active" &&
                    c.NotAfter <= DateTimeOffset.UtcNow.AddDays(template.RenewalPeriodDays) &&
                    c.Subject.Contains(hostname, StringComparison.OrdinalIgnoreCase));

                if (expiring != null)
                {
                    _logger.LogInformation("Renewing expiring certificate {Serial} for template {Template}",
                        expiring.SerialNumber, template.Name);
                    try
                    {
                        await _caService.RenewCertificateAsync(expiring.SerialNumber);
                        _logger.LogInformation("Auto-renewed certificate for template {Template}", template.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to auto-renew certificate for template {Template}", template.Name);
                    }
                    continue;
                }

                // No certificate at all — issue a new one
                _logger.LogInformation("Auto-enrolling certificate for template {Template}", template.Name);
                try
                {
                    var request = new CertificateIssuanceRequest
                    {
                        TemplateName = template.Name,
                        SubjectDn = $"CN={hostname}",
                        SubjectAlternativeNames = [$"dns:{hostname}"],
                        RequestedBy = "SYSTEM (Auto-Enrollment)",
                    };

                    await _caService.IssueCertificateAsync(request);
                    _logger.LogInformation("Auto-enrolled certificate for template {Template}", template.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to auto-enroll certificate for template {Template}", template.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-enrollment check failed");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
