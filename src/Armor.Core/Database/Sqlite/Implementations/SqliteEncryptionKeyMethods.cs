namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Helpers;
    using Armor.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IEncryptionKeyMethods"/>.
    /// </summary>
    public sealed class SqliteEncryptionKeyMethods : IEncryptionKeyMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteEncryptionKeyMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteEncryptionKeyMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<EncryptionKey> CreateAsync(EncryptionKey key, CancellationToken token = default)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (String.IsNullOrWhiteSpace(key.Id))
                key.Id = IdGenerator.GenerateEncryptionKeyId();

            await _Driver.ExecuteQueryAsync(
                "INSERT INTO encryption_keys (id, name, cipher_name, kdf_name, kdf_iterations, kdf_salt_b64, uses_passphrase, uses_keyfile, passphrase_wrapped_b64, keyfile_wrapped_b64, created_utc) VALUES (" +
                Sanitizer.Literal(key.Id) + ", " +
                Sanitizer.Literal(key.Name) + ", " +
                Sanitizer.Literal(key.CipherName) + ", " +
                Sanitizer.Literal(key.KdfName) + ", " +
                Sanitizer.Int(key.KdfIterations) + ", " +
                Sanitizer.Quote(key.KdfSaltBase64) + ", " +
                Sanitizer.Bool(key.UsesPassphrase) + ", " +
                Sanitizer.Bool(key.UsesKeyFile) + ", " +
                Sanitizer.Quote(key.PassphraseWrappedKeyBase64) + ", " +
                Sanitizer.Quote(key.KeyFileWrappedKeyBase64) + ", " +
                Sanitizer.Timestamp(key.CreatedUtc) + ");", false, token).ConfigureAwait(false);

            return key;
        }

        /// <inheritdoc/>
        public async Task<EncryptionKey?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM encryption_keys WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<List<EncryptionKey>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM encryption_keys ORDER BY created_utc ASC, id ASC;", false, token).ConfigureAwait(false);

            List<EncryptionKey> list = new List<EncryptionKey>();
            foreach (DataRow row in table.Rows)
                list.Add(MapRow(row));
            return list;
        }

        /// <inheritdoc/>
        public async Task<EncryptionKey> UpdateAsync(EncryptionKey key, CancellationToken token = default)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (String.IsNullOrWhiteSpace(key.Id))
                throw new ArgumentException("Encryption key id is required for update.", nameof(key));

            await _Driver.ExecuteQueryAsync(
                "UPDATE encryption_keys SET " +
                "name = " + Sanitizer.Literal(key.Name) + ", " +
                "cipher_name = " + Sanitizer.Literal(key.CipherName) + ", " +
                "kdf_name = " + Sanitizer.Literal(key.KdfName) + ", " +
                "kdf_iterations = " + Sanitizer.Int(key.KdfIterations) + ", " +
                "kdf_salt_b64 = " + Sanitizer.Quote(key.KdfSaltBase64) + ", " +
                "uses_passphrase = " + Sanitizer.Bool(key.UsesPassphrase) + ", " +
                "uses_keyfile = " + Sanitizer.Bool(key.UsesKeyFile) + ", " +
                "passphrase_wrapped_b64 = " + Sanitizer.Quote(key.PassphraseWrappedKeyBase64) + ", " +
                "keyfile_wrapped_b64 = " + Sanitizer.Quote(key.KeyFileWrappedKeyBase64) + " " +
                "WHERE id = " + Sanitizer.Literal(key.Id) + ";", false, token).ConfigureAwait(false);

            return key;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (!await ExistsAsync(id, token).ConfigureAwait(false))
                return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM encryption_keys WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM encryption_keys WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        private static EncryptionKey MapRow(DataRow row)
        {
            EncryptionKey key = new EncryptionKey();
            key.Id = Converters.GetString(row, "id");
            key.Name = Converters.GetString(row, "name");
            key.CipherName = Converters.GetString(row, "cipher_name");
            key.KdfName = Converters.GetString(row, "kdf_name");
            key.KdfIterations = Converters.GetInt(row, "kdf_iterations");
            key.KdfSaltBase64 = Converters.GetStringOrNull(row, "kdf_salt_b64");
            key.UsesPassphrase = Converters.GetBool(row, "uses_passphrase");
            key.UsesKeyFile = Converters.GetBool(row, "uses_keyfile");
            key.PassphraseWrappedKeyBase64 = Converters.GetStringOrNull(row, "passphrase_wrapped_b64");
            key.KeyFileWrappedKeyBase64 = Converters.GetStringOrNull(row, "keyfile_wrapped_b64");
            key.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return key;
        }
    }
}
