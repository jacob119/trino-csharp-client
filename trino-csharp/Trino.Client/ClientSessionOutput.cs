using System.Collections.Generic;

namespace Trino.Client
{
    /// <summary>
    /// Contains client session properties that are received from Trino response headers.
    /// </summary>
    internal class ClientSessionOutput
    {
        public ClientSessionOutput()
        {
            this.ResponseAddedPrepare = new Dictionary<string, string>();
            this.ResponseDeallocatedPrepare = new Dictionary<string, string>();
            this.ClearSessionProperties = new HashSet<string>();
            this.SetRoles = new Dictionary<string, ClientSelectedRole>();
        }

        internal string SetCatalog { get; set; }
        internal string SetSchema { get; set; }
        internal string SetPath { get; set; }
        internal string SetAuthorizationUser { get; set; }
        internal bool ResetAuthorizationUser { get; set; }
        internal Dictionary<string, string> SetSessionProperties { get; set; } = new Dictionary<string, string>();
        internal HashSet<string> ClearSessionProperties { get; set; }
        internal Dictionary<string, ClientSelectedRole> SetRoles { get; set; }
        internal string StartedTransactionId { get; set; }
        internal bool ClearTransactionId { get; set; }
        internal HashSet<string> SetOriginalRoles { get; set; } = new HashSet<string>();
        internal Dictionary<string, string> ResponseAddedPrepare { get; set; }
        internal Dictionary<string, string> ResponseDeallocatedPrepare { get; set; }
    }
}
