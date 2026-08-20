namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal assertion helpers for Touchstone descriptors. Each method throws on failure; the
    /// runner treats a thrown exception as a failed case.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Assert that a condition is true.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert that a condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is true.</exception>
        public static void False(bool condition, string message)
        {
            if (condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert that two values are equal using the default comparer.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the values differ.</exception>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Assertion failed: " + message + " (expected '" + expected + "', actual '" + actual + "')");
        }

        /// <summary>
        /// Assert that a reference is not null.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value is null.</exception>
        public static void NotNull(object? value, string message)
        {
            if (value == null)
                throw new InvalidOperationException("Assertion failed (expected non-null): " + message);
        }

        /// <summary>
        /// Assert that a reference is null.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value is not null.</exception>
        public static void Null(object? value, string message)
        {
            if (value != null)
                throw new InvalidOperationException("Assertion failed (expected null): " + message);
        }

        /// <summary>
        /// Assert that an asynchronous action throws an exception of the expected type.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The action.</param>
        /// <param name="message">Failure message.</param>
        /// <returns>A task that completes when the assertion is verified.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no exception, or the wrong type, is thrown.</exception>
        public static async Task ThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Assertion failed: " + message + " (expected " + typeof(TException).Name + ", got " + ex.GetType().Name + ")");
            }

            throw new InvalidOperationException("Assertion failed: " + message + " (expected " + typeof(TException).Name + ", but nothing was thrown)");
        }
    }
}
