namespace Armor.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Storage;

    /// <summary>
    /// Decorates an <see cref="IStorageRepository"/> with integration telemetry: every call records
    /// <see cref="TelemetryNames.StorageOperations"/> (by target type, operation, and outcome),
    /// <see cref="TelemetryNames.StorageOperationDuration"/>, the in-flight gauge, and bytes sent or
    /// received. Metadata-object calls also open a client span named <c>&lt;type&gt; &lt;operation&gt;</c>
    /// (for example <c>amazon_s3 write_object</c>); chunk calls do so only when
    /// <see cref="ArmorTelemetry.TraceChunkOperations"/> is on. Behavior and exceptions of the inner
    /// repository are passed through unchanged. Thread-safe when the inner repository is.
    /// </summary>
    public sealed class InstrumentedStorageRepository : IStorageRepository
    {
        #region Public-Members

        /// <summary>
        /// The wrapped repository.
        /// </summary>
        public IStorageRepository Inner
        {
            get { return _Inner; }
        }

        /// <summary>
        /// The bounded storage-type label recorded on every measurement (for example disk or amazon_s3).
        /// </summary>
        public string StorageType
        {
            get { return _StorageType; }
        }

        #endregion

        #region Private-Members

        private readonly IStorageRepository _Inner;
        private readonly string _StorageType;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentedStorageRepository"/> class.
        /// </summary>
        /// <param name="inner">The repository to decorate. Cannot be null.</param>
        /// <param name="storageType">The bounded storage-type label, normally from <see cref="ArmorTelemetry.StorageTypeLabel"/>. Cannot be null or whitespace.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or whitespace.</exception>
        public InstrumentedStorageRepository(IStorageRepository inner, string storageType)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (String.IsNullOrWhiteSpace(storageType))
                throw new ArgumentNullException(nameof(storageType));
            _StorageType = storageType;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> ValidateConnectionAsync(CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpValidate, null, false);
            try
            {
                bool ok = await _Inner.ValidateConnectionAsync(token).ConfigureAwait(false);
                if (ok)
                    End(TelemetryNames.OpValidate, start, activity, null, null);
                else
                    EndMismatch(start, activity);
                return ok;
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpValidate, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task WriteObjectAsync(string key, byte[] data, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpWriteObject, key, false);
            try
            {
                await _Inner.WriteObjectAsync(key, data, token).ConfigureAwait(false);
                AddBytes(TelemetryNames.DirectionSent, data?.Length ?? 0);
                End(TelemetryNames.OpWriteObject, start, activity, null, null);
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpWriteObject, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadObjectAsync(string key, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpReadObject, key, false);
            try
            {
                byte[] data = await _Inner.ReadObjectAsync(key, token).ConfigureAwait(false);
                AddBytes(TelemetryNames.DirectionReceived, data?.Length ?? 0);
                End(TelemetryNames.OpReadObject, start, activity, null, null);
                return data!;
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpReadObject, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ObjectExistsAsync(string key, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpObjectExists, key, false);
            try
            {
                bool exists = await _Inner.ObjectExistsAsync(key, token).ConfigureAwait(false);
                End(TelemetryNames.OpObjectExists, start, activity, null, exists);
                return exists;
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpObjectExists, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DeleteObjectAsync(string key, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpDeleteObject, key, false);
            try
            {
                await _Inner.DeleteObjectAsync(key, token).ConfigureAwait(false);
                End(TelemetryNames.OpDeleteObject, start, activity, null, null);
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpDeleteObject, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> EnumerateKeysAsync(string prefix, [EnumeratorCancellation] CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpEnumerate, prefix, false);
            Exception? failure = null;
            long keys = 0;
            IAsyncEnumerator<string> enumerator = _Inner.EnumerateKeysAsync(prefix, token).GetAsyncEnumerator(token);
            try
            {
                while (true)
                {
                    string current;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                            break;
                        current = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        throw;
                    }

                    keys++;
                    yield return current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
                ArmorTelemetry.SetTag(activity, "armor.storage.keys", keys);
                End(TelemetryNames.OpEnumerate, start, activity, failure, null);
            }
        }

        /// <inheritdoc />
        public async Task WriteChunkAsync(string hashHex, byte[] stored, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpWriteChunk, hashHex, true);
            try
            {
                await _Inner.WriteChunkAsync(hashHex, stored, token).ConfigureAwait(false);
                AddBytes(TelemetryNames.DirectionSent, stored?.Length ?? 0);
                End(TelemetryNames.OpWriteChunk, start, activity, null, null);
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpWriteChunk, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadChunkAsync(string hashHex, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpReadChunk, hashHex, true);
            try
            {
                byte[] data = await _Inner.ReadChunkAsync(hashHex, token).ConfigureAwait(false);
                AddBytes(TelemetryNames.DirectionReceived, data?.Length ?? 0);
                End(TelemetryNames.OpReadChunk, start, activity, null, null);
                return data!;
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpReadChunk, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ChunkExistsAsync(string hashHex, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpChunkExists, hashHex, true);
            try
            {
                bool exists = await _Inner.ChunkExistsAsync(hashHex, token).ConfigureAwait(false);
                End(TelemetryNames.OpChunkExists, start, activity, null, exists);
                return exists;
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpChunkExists, start, activity, ex, null);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DeleteChunkAsync(string hashHex, CancellationToken token = default)
        {
            long start = Stopwatch.GetTimestamp();
            Activity? activity = Begin(TelemetryNames.OpDeleteChunk, hashHex, true);
            try
            {
                await _Inner.DeleteChunkAsync(hashHex, token).ConfigureAwait(false);
                End(TelemetryNames.OpDeleteChunk, start, activity, null, null);
            }
            catch (Exception ex)
            {
                End(TelemetryNames.OpDeleteChunk, start, activity, ex, null);
                throw;
            }
        }

        #endregion

        #region Private-Methods

        private Activity? Begin(string operation, string? key, bool chunk)
        {
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStorageType, _StorageType);
            ArmorTelemetry.Add(ArmorTelemetry.StorageOperationsActive, 1, tags);

            if (chunk && !ArmorTelemetry.TraceChunkOperations)
                return null;
            if (!ArmorTelemetry.ActivitySource.HasListeners())
                return null;

            Activity? activity = ArmorTelemetry.StartActivity(_StorageType + " " + operation, ActivityKind.Client);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrStorageType, _StorageType);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrStorageOperation, operation);
            if (!String.IsNullOrEmpty(key))
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrStorageKey, key);
            return activity;
        }

        private void End(string operation, long start, Activity? activity, Exception? failure, bool? exists)
        {
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            string outcome = failure == null ? TelemetryNames.OutcomeSuccess : ArmorTelemetry.OutcomeOf(failure);
            string? errorType = failure != null && outcome == TelemetryNames.OutcomeFailure ? ArmorTelemetry.ErrorType(failure) : null;

            TagList active = new TagList();
            active.Add(TelemetryNames.AttrStorageType, _StorageType);
            ArmorTelemetry.Add(ArmorTelemetry.StorageOperationsActive, -1, active);

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStorageType, _StorageType);
            tags.Add(TelemetryNames.AttrStorageOperation, operation);
            tags.Add(TelemetryNames.AttrOutcome, outcome);
            ArmorTelemetry.Record(ArmorTelemetry.StorageOperationDuration, seconds, tags);
            if (errorType != null)
                tags.Add(TelemetryNames.AttrErrorType, errorType);
            ArmorTelemetry.Add(ArmorTelemetry.StorageOperations, 1, tags);

            if (failure != null)
            {
                if (errorType != null)
                    ArmorTelemetry.RecordError(TelemetryNames.ComponentStorage, failure);
                ArmorTelemetry.MarkException(activity, failure);
            }
            else
            {
                if (exists.HasValue)
                    ArmorTelemetry.SetTag(activity, "armor.storage.exists", exists.Value);
                ArmorTelemetry.MarkSuccess(activity);
            }
            ArmorTelemetry.Stop(activity);
        }

        private void EndMismatch(long start, Activity? activity)
        {
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;

            TagList active = new TagList();
            active.Add(TelemetryNames.AttrStorageType, _StorageType);
            ArmorTelemetry.Add(ArmorTelemetry.StorageOperationsActive, -1, active);

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStorageType, _StorageType);
            tags.Add(TelemetryNames.AttrStorageOperation, TelemetryNames.OpValidate);
            tags.Add(TelemetryNames.AttrOutcome, TelemetryNames.OutcomeFailure);
            ArmorTelemetry.Record(ArmorTelemetry.StorageOperationDuration, seconds, tags);
            tags.Add(TelemetryNames.AttrErrorType, "ValidationMismatch");
            ArmorTelemetry.Add(ArmorTelemetry.StorageOperations, 1, tags);

            if (activity != null)
            {
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrOutcome, TelemetryNames.OutcomeFailure);
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrErrorType, "ValidationMismatch");
                try
                {
                    activity.SetStatus(ActivityStatusCode.Error, "The probe object read back did not match what was written.");
                }
                catch (Exception)
                {
                    // Best-effort.
                }
            }
            ArmorTelemetry.Stop(activity);
        }

        private void AddBytes(string direction, long count)
        {
            if (count <= 0)
                return;
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStorageType, _StorageType);
            tags.Add(TelemetryNames.AttrStorageDirection, direction);
            ArmorTelemetry.Add(ArmorTelemetry.StorageBytes, count, tags);
        }

        #endregion
    }
}
