namespace Directory.Server.Setup;

public class SetupWizard
{
    public static SetupOptions Run(string[] args)
    {
        var options = new SetupOptions();

        // Parse command-line arguments first (for non-interactive/scripted use)
        ParseArgs(args, options);

        // If required fields missing, prompt interactively
        Console.WriteLine();
        if (options.IsReplica)
        {
            Console.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
            Console.WriteLine("\u2551          Active Directory Domain Services Setup          \u2551");
            Console.WriteLine("\u2551               Replica Domain Controller Setup             \u2551");
            Console.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
        }
        else
        {
            Console.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
            Console.WriteLine("\u2551          Active Directory Domain Services Setup          \u2551");
            Console.WriteLine("\u2551                   Domain Provisioning                    \u2551");
            Console.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
        }
        Console.WriteLine();

        if (options.IsReplica)
        {
            // ── Replica mode: prompt for source DC and credentials ──
            PromptReplicaOptions(options);
        }
        else
        {
            // ── New domain mode: prompt for domain name and admin password ──
            PromptNewDomainOptions(options);
        }

        // Show summary and confirm
        Console.WriteLine();
        Console.WriteLine("  \u2500\u2500\u2500 Domain Configuration Summary \u2500\u2500\u2500");
        Console.WriteLine($"  Mode:             {(options.IsReplica ? "Replica DC (join existing domain)" : "New Forest/Domain")}");
        Console.WriteLine($"  Domain Name:      {options.DomainName}");
        Console.WriteLine($"  NetBIOS Name:     {options.NetBiosName}");
        Console.WriteLine($"  Forest Name:      {options.ForestName}");
        Console.WriteLine($"  Domain DN:        {options.DomainDn}");
        if (options.IsReplica)
        {
            Console.WriteLine($"  Source DC:        {options.SourceDcUrl}");
            Console.WriteLine($"  Repl. Admin:      {options.ReplicationAdminUpn}");
        }
        else
        {
            Console.WriteLine($"  Admin Account:    {options.AdminUsername}");
        }
        Console.WriteLine($"  Site:             {options.SiteName}");
        Console.WriteLine($"  Hostname:         {options.Hostname}");
        Console.WriteLine($"  Functional Level: Windows Server 2016");
        Console.WriteLine();
        Console.Write($"  Proceed with {(options.IsReplica ? "replica" : "domain")} provisioning? [Y/n]: ");

        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (response == "n" || response == "no")
        {
            Console.WriteLine("  Setup cancelled.");
            Environment.Exit(0);
        }

        return options;
    }

    /// <summary>
    /// Prompts the user for source DC URL, admin credentials, and domain name when
    /// joining an existing domain as a replica DC.
    /// </summary>
    private static void PromptReplicaOptions(SetupOptions options)
    {
        // Source DC URL
        if (string.IsNullOrEmpty(options.SourceDcUrl))
        {
            Console.Write("  Source DC URL (e.g., https://dc1.corp.com:9389): ");
            options.SourceDcUrl = Console.ReadLine()?.Trim() ?? "";

            while (string.IsNullOrWhiteSpace(options.SourceDcUrl) || !IsValidUrl(options.SourceDcUrl))
            {
                Console.Write("  Invalid URL. Enter a valid source DC URL: ");
                options.SourceDcUrl = Console.ReadLine()?.Trim() ?? "";
            }
        }

        // Replication admin UPN
        if (string.IsNullOrEmpty(options.ReplicationAdminUpn))
        {
            Console.Write("  Admin UPN for replication (e.g., administrator@corp.com): ");
            options.ReplicationAdminUpn = Console.ReadLine()?.Trim() ?? "";

            while (string.IsNullOrWhiteSpace(options.ReplicationAdminUpn))
            {
                Console.Write("  Admin UPN cannot be empty: ");
                options.ReplicationAdminUpn = Console.ReadLine()?.Trim() ?? "";
            }
        }

        // Replication admin password
        if (string.IsNullOrEmpty(options.ReplicationAdminPassword))
        {
            Console.Write("  Admin password: ");
            options.ReplicationAdminPassword = ReadPassword();
            Console.WriteLine();

            while (string.IsNullOrWhiteSpace(options.ReplicationAdminPassword))
            {
                Console.Write("  Password cannot be empty: ");
                options.ReplicationAdminPassword = ReadPassword();
                Console.WriteLine();
            }
        }

        // Domain name — required so we know which domain to join
        if (string.IsNullOrEmpty(options.DomainName))
        {
            Console.Write("  Domain name to join (e.g., corp.com): ");
            options.DomainName = Console.ReadLine()?.Trim() ?? "";
        }

        while (!IsValidDomainName(options.DomainName))
        {
            Console.Write("  Invalid domain name. Enter a valid DNS name (e.g., corp.com): ");
            options.DomainName = Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(options.NetBiosName))
            options.NetBiosName = options.DomainName.Split('.')[0].ToUpperInvariant();

        if (string.IsNullOrEmpty(options.ForestName))
            options.ForestName = options.DomainName;

        // Admin password is not prompted in replica mode — we use replication credentials
        if (string.IsNullOrEmpty(options.AdminPassword))
            options.AdminPassword = options.ReplicationAdminPassword ?? "";
    }

    /// <summary>
    /// Prompts the user for domain name, NetBIOS name, and administrator password
    /// when creating a new forest/domain.
    /// </summary>
    private static void PromptNewDomainOptions(SetupOptions options)
    {
        if (string.IsNullOrEmpty(options.DomainName))
        {
            Console.Write("  Domain name (e.g., contoso.com): ");
            options.DomainName = Console.ReadLine()?.Trim() ?? "";
        }

        // Validate domain name format
        while (!IsValidDomainName(options.DomainName))
        {
            Console.Write("  Invalid domain name. Enter a valid DNS name (e.g., contoso.com): ");
            options.DomainName = Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(options.NetBiosName))
        {
            // Default to first label of domain name, uppercased
            var defaultNetbios = options.DomainName.Split('.')[0].ToUpperInvariant();
            Console.Write($"  NetBIOS domain name [{defaultNetbios}]: ");
            var input = Console.ReadLine()?.Trim();
            options.NetBiosName = string.IsNullOrEmpty(input) ? defaultNetbios : input.ToUpperInvariant();
        }

        if (string.IsNullOrEmpty(options.AdminPassword))
        {
            // Prompt for password (masked)
            Console.Write("  Administrator password: ");
            options.AdminPassword = ReadPassword();
            Console.WriteLine();

            // Confirm
            Console.Write("  Confirm password: ");
            var confirm = ReadPassword();
            Console.WriteLine();

            while (options.AdminPassword != confirm)
            {
                Console.WriteLine("  Passwords do not match. Try again.");
                Console.Write("  Administrator password: ");
                options.AdminPassword = ReadPassword();
                Console.WriteLine();
                Console.Write("  Confirm password: ");
                confirm = ReadPassword();
                Console.WriteLine();
            }

            // Validate complexity
            while (!MeetsComplexity(options.AdminPassword))
            {
                Console.WriteLine("  Password must be at least 7 characters with 3+ character categories (upper, lower, digit, symbol).");
                Console.Write("  Administrator password: ");
                options.AdminPassword = ReadPassword();
                Console.WriteLine();
            }
        }

        if (string.IsNullOrEmpty(options.ForestName))
            options.ForestName = options.DomainName;
    }

    /// <summary>
    /// Validates whether a string is a well-formed URL.
    /// </summary>
    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.Enter)
                break;
            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                password.Append(keyInfo.KeyChar);
                Console.Write('*');
            }
        }
        return password.ToString();
    }

    private static bool IsValidDomainName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Must have at least one dot
        var labels = name.Split('.');
        if (labels.Length < 2)
            return false;

        foreach (var label in labels)
        {
            if (string.IsNullOrEmpty(label) || label.Length > 63)
                return false;

            // Must start/end with alphanumeric, can contain hyphens
            if (!char.IsLetterOrDigit(label[0]) || !char.IsLetterOrDigit(label[^1]))
                return false;

            foreach (var c in label)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        return true;
    }

    private static bool MeetsComplexity(string password)
    {
        if (password.Length < 7)
            return false;

        int categories = 0;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) categories++;

        return categories >= 3;
    }

    private static void ParseArgs(string[] args, SetupOptions options)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var key = args[i].ToLowerInvariant();

            // Flag-only arguments (no value)
            if (key == "--replica")
            {
                options.IsReplica = true;
                continue;
            }

            // Key-value arguments
            if (i + 1 >= args.Length) continue;
            var value = args[i + 1];

            switch (key)
            {
                case "--domain":
                    options.DomainName = value;
                    i++;
                    break;
                case "--netbios":
                    options.NetBiosName = value.ToUpperInvariant();
                    i++;
                    break;
                case "--admin-password":
                    options.AdminPassword = value;
                    i++;
                    break;
                case "--tenant-id":
                    options.TenantId = value;
                    i++;
                    break;
                case "--admin-user":
                    options.AdminUsername = value;
                    i++;
                    break;
                case "--site-name":
                    options.SiteName = value;
                    i++;
                    break;
                case "--forest-name":
                    options.ForestName = value;
                    i++;
                    break;
                case "--hostname":
                    options.Hostname = value;
                    i++;
                    break;
                case "--cosmos-connection":
                    options.CosmosConnectionString = value;
                    i++;
                    break;
                case "--cosmos-database":
                    options.CosmosDatabaseName = value;
                    i++;
                    break;
                case "--source-dc":
                    options.SourceDcUrl = value;
                    i++;
                    break;
                case "--repl-admin":
                    options.ReplicationAdminUpn = value;
                    i++;
                    break;
                case "--repl-password":
                    options.ReplicationAdminPassword = value;
                    i++;
                    break;
            }
        }
    }
}
