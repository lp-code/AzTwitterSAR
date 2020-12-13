using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;


namespace DurableAzTwitterSar
{
    class KeyVaultAccessor
    {
        private KeyVaultAccessor()
        {
            string keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
            var kvUri = "https://" + keyVaultName + ".vault.azure.net";
            SecretClientOptions options = new SecretClientOptions()
            {
                Retry =
                {
                    Delay= TimeSpan.FromSeconds(2),
                    MaxDelay = TimeSpan.FromSeconds(16),
                    MaxRetries = 5,
                    Mode = RetryMode.Exponential
                }
            };
            client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential(), options);
        }

        private static KeyVaultAccessor _instance;
        private static readonly object _lock = new object();
        private SecretClient client;

        public static KeyVaultAccessor GetInstance()
        {
            // C# thread-safe singleton pattern from refactoring.guru
            // This conditional is needed to prevent threads stumbling over the
            // lock once the instance is ready.
            if (_instance == null)
            {
                lock (_lock)
                {
                    // The first thread to acquire the lock, reaches this
                    // conditional, goes inside and creates the Singleton
                    // instance. Once it leaves the lock block, a thread that
                    // might have been waiting for the lock release may then
                    // enter this section. But since the Singleton field is
                    // already initialized, the thread won't create a new
                    // object.
                    if (_instance == null)
                    {
                        _instance = new KeyVaultAccessor();
                    }
                }
            }
            return _instance;
        }

        public async Task<string> GetSecretAsync(string secretName) 
        {
            string secret = "";
            try
            {
                secret = (await client.GetSecretAsync(secretName)).Value.Value;
            }
            catch
            {
                // During local testing we have no access to the Azure Key Vault, so we
                // read from the environment variable instead.
                secret = Environment.GetEnvironmentVariable(secretName);
            }

            return secret;
        }
    }
}
