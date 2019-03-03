using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;

namespace Kamus.KeyManagement
{
    public class AwsKeyManagement : IKeyManagement
    {
        private readonly IAmazonKeyManagementService mAmazonKeyManagementService;
        private readonly SymmetricKeyManagement mSymmetricKeyManagement;

        public AwsKeyManagement(IAmazonKeyManagementService amazonKeyManagementService, SymmetricKeyManagement symmetricKeyManagement)
        {
            mAmazonKeyManagementService = amazonKeyManagementService;
            mSymmetricKeyManagement = symmetricKeyManagement;
        }

        public async Task<string> Encrypt(string data, string serviceAccountId, bool createKeyIfMissing = true)
        {
            var masterKeyAlias = $"alias/kamus/{KeyIdCreator.Create(serviceAccountId)}";
            (var encryptionKey, var encryptedEncryptionKey ) = await GenerateEncryptionKey(masterKeyAlias);
            mSymmetricKeyManagement.SetEncryptionKey(Convert.ToBase64String(encryptionKey.ToArray()));
            var encryptedData = await mSymmetricKeyManagement.Encrypt(data, serviceAccountId);

            return "env" + "$" + encryptedEncryptionKey + "$" + encryptedData;

        }

        public async Task<string> Decrypt(string encryptedData, string serviceAccountId)
        {
            var encryptedEncryptionKey = encryptedData.Split('$')[1];
            var actualEncryptedData = encryptedData.Split('$')[2];
            
            var decryptionResult = await mAmazonKeyManagementService.DecryptAsync(new DecryptRequest
            {
                CiphertextBlob = new MemoryStream(Convert.FromBase64String(encryptedEncryptionKey)),
            });

            var encryptionKey = await ConvertMemoryStreamToBase64String(decryptionResult.Plaintext);
                
            mSymmetricKeyManagement.SetEncryptionKey(encryptionKey);
            return await mSymmetricKeyManagement.Decrypt(actualEncryptedData, serviceAccountId);
        }

        private static async Task<string> ConvertMemoryStreamToBase64String(MemoryStream ms)
        {
            return Convert.ToBase64String(ms.ToArray());
        }

        private async Task<(MemoryStream encryptionKey, string encryptedEncryptionKey)> GenerateEncryptionKey(string keyAlias)
        {
            GenerateDataKeyResponse generateKeyResponse = null;
            try
            {
                generateKeyResponse = await mAmazonKeyManagementService.GenerateDataKeyAsync(new GenerateDataKeyRequest { KeyId = keyAlias, KeySpec = "AES_256"});

            }
            catch (NotFoundException e)
            {
                await GenerateMasterKey(keyAlias);
                generateKeyResponse = await mAmazonKeyManagementService.GenerateDataKeyAsync(new GenerateDataKeyRequest { KeyId = keyAlias, KeySpec = "AES_256"});
            }

            if (generateKeyResponse.HttpStatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Couldn't generate encryption key for {keyAlias}");
            }

            return (generateKeyResponse.Plaintext, await ConvertMemoryStreamToBase64String(generateKeyResponse.CiphertextBlob));
        }

        private async Task GenerateMasterKey(string keyAlias)
        {
            var createKeyResponse = await mAmazonKeyManagementService.CreateKeyAsync(new CreateKeyRequest());
            await mAmazonKeyManagementService.CreateAliasAsync(keyAlias, createKeyResponse.KeyMetadata.KeyId);
        }
    }
}