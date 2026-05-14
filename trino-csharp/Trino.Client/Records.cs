using Trino.Client.Logging;
using Trino.Client.Model;
using Trino.Client.Model.StatementV1;
using Trino.Client.Utils;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Trino.Client
{
    /// <summary>
    /// Record iterator for Trino query results.
    /// </summary>
    public class Records : IEnumerator<List<object>>, IAsyncEnumeratorPlaceholder<List<object>>, IDisposable
    {
        /// <summary>
        /// The iterator over the pages
        /// </summary>
        private readonly Pages pages;

        /// <summary>
        /// The current Trino page.
        /// </summary>
        private QueryResultPage currentPage;

        /// <summary>
        /// The index on the current page
        /// </summary>
        private int rowIndex = 0;

        /// <summary>
        /// Forces the reader to return only one row.
        /// </summary>
        private readonly bool forceLimitOne = false;
        private readonly ILoggerWrapper logger;
        private bool isClosed;
        private int _disposeFlag;
        private IList<TrinoColumn> columns; // accessed via Volatile.Read/Write

        internal Records(ILoggerWrapper logger, Pages pages)
        {
            this.logger = logger;
            this.pages = pages;
        }

        /// <summary>
        /// The current row of the current page.
        /// </summary>
        public List<object> Current => currentPage.data[rowIndex];

        /// <summary>
        /// The current row of the current page.
        /// </summary>
        object IEnumerator.Current => Current;

        /// <summary>
        /// The executing query stats (continually updated)
        /// </summary>
        public TrinoStats Stats => currentPage?.stats;

        public IList<TrinoColumn> Columns
        {
            get
            {
                var cols = Volatile.Read(ref columns);
                if (cols != null) return cols;
                cols = PopulateColumnsAsync().SafeResult();
                Volatile.Write(ref columns, cols);
                return cols;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeFlag, 1) == 0)
            {
                Volatile.Write(ref isClosed, true);
                pages.Dispose();
            }
        }

        public bool MoveNext()
        {
            return MoveNextAsync(System.Threading.CancellationToken.None).SafeResult();
        }

        Task<bool> IAsyncEnumeratorPlaceholder<List<object>>.MoveNextAsync() =>
            MoveNextAsync(System.Threading.CancellationToken.None);

        public async Task<bool> MoveNextAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            logger?.LogDebug($"Trino IDataReader: attempt to read row. Page index: {rowIndex}.");
            if (Volatile.Read(ref isClosed))
            {
                throw new TrinoException("Reading a stream that is already closed");
            }

            // If starting the first page, or at the end of a page, fetch the next page
            if (pages.IsEmptyPage || IsAtEndOfPage())
            {
                // If there is a next page, advance to the first row
                if (await pages.MoveNextAsync(cancellationToken).ConfigureAwait(false) && pages.Current.HasData)
                {
                    logger?.LogDebug($"Trino IDataReader: Start on row 0");
                    rowIndex = 0;
                    currentPage = pages.Current;

                    if (columns == null)
                    {
                        columns = pages.Current.columns;
                    }
                }
                else if (pages.IsFinished())
                {
                    logger?.LogDebug($"Trino IDataReader: No more pages");
                    Volatile.Write(ref isClosed, true);
                    return false;
                }
                else
                {
                    throw new InvalidOperationException("Trino client should never serve an empty page that is not the last page.");
                }
            }
            else
            {
                // ADO.net requirement to limit the read to a single row for specific calls.
                if (forceLimitOne)
                {
                    logger?.LogInformation($"Trino IDataReader: Force limit one row");
                    Volatile.Write(ref isClosed, true);
                    pages.Dispose();
                    return false;
                }

                // if within a page, advance to the next row
                rowIndex++;
            }

            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException("Trino stream is read forward only");
        }

        /// <summary>
        /// Get value from column at index i.
        /// </summary>
        public T GetValue<T>(int i, bool allowNull = true)
        {
            return CastWithNullCheck<T>(i, TrinoTypeConverters.ConvertToTrinoTypeFromJson(Current[i], GetColumn(i).type), allowNull);
        }

        /// <summary>
        /// Get value from column at index i and validate the type.
        /// </summary>
        public T GetValue<T>(int i, string validType, bool allowNull = true)
        {
            return CastWithNullCheck<T>(i, TrinoTypeConverters.ConvertToTrinoTypeFromJson(Current[i], GetColumn(i).type, validType), allowNull);
        }

        /// <summary>
        /// Get value from column at index i and validate the type against a list of types.
        /// </summary>
        public T GetValue<T>(int i, string[] validTypes, bool allowNull = true)
        {
            return CastWithNullCheck<T>(i, TrinoTypeConverters.ConvertToTrinoTypeFromJson(Current[i], GetColumn(i).type, validTypes), allowNull);
        }

        public TrinoColumn GetColumn(int i)
        {
            if (i < 0 || i >= Columns.Count)
            {
                throw new ArgumentException("Requested column index out of range.");
            }
            return Columns[i];
        }

        public async Task<bool> HasData()
        {
            return await pages.HasData().ConfigureAwait(false);
        }

        public async Task<Statement> ReadToEnd()
        {
            return await pages.ReadToEnd().ConfigureAwait(false);
        }

        /// <summary>
        /// Updates columns. Used to fetch columns if they are read before rows are read.
        /// </summary>
        internal async Task<IList<TrinoColumn>> PopulateColumnsAsync()
        {
            if (columns == null)
            {
                columns = await pages.WaitAndGetColumns().ConfigureAwait(false);
            }
            return columns;
        }

        private T CastWithNullCheck<T>(int colIndex, object o, bool allowNull)
        {
            if (o == null)
            {
                if (!allowNull)
                    throw new NullReferenceException($"Value in column {colIndex} of type `{this.Columns[colIndex].type}` is null, and requested type `{typeof(T).Name}` is not nullable.");
                return default;
            }

            // Pattern-match avoids throwing InvalidCastException on every type mismatch.
            if (o is T direct)
                return direct;

            try
            {
                return (T)Convert.ChangeType(o, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                throw new InvalidCastException($"Cannot cast or convert object of type `{o.GetType()}` to type `{typeof(T)}`.");
            }
        }

        private bool IsAtEndOfPage()
        {
            return rowIndex == pages.Current.data.Count - 1;
        }
    }
}
