﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using RavenDB.AspNetCore.DependencyInjection.Exceptions;
using RavenDB.AspNetCore.DependencyInjection.Helpers;
using RavenDB.AspNetCore.DependencyInjection.Options;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace RavenDB.AspNetCore.DependencyInjection
{
    /// <summary>
    /// Represents a class responsible for the managing of sessions and stores.
    /// </summary>
    public class RavenManager
         : IRavenManager, IDisposable
    {
        /// <summary>
        /// The database used when no specific database is specified.
        /// </summary>
        public string DefaultServer { get; private set; }

        /// <summary>
        /// The conventions used when no specific conventions are specified.
        /// </summary>
        public DocumentConventions DefaultConventions { get; private set; }

        private bool _disposed;
        private ConcurrentDictionary<string, Lazy<IDocumentStore>> _stores;
        private ConcurrentDictionary<string, RavenStoreOptions> _servers;
        private IHostingEnvironment _host;

        /// <summary>
        /// Initializes a new instance of the RavenManager class with specified options.
        /// </summary>
        /// <param name="options">Options that are used to configuration the RavenManager.</param>
        /// <param name="host">The hosting environment. This is used to fetch the certificate file used to connect to the database.</param>
        public RavenManager(
            IOptions<RavenManagerOptions> options,
            IHostingEnvironment host)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _disposed = false;
            DefaultServer = options.Value.DefaultServer ??
                options.Value.Servers.Keys.FirstOrDefault();
            DefaultConventions = options.Value.DefaultConventions != null ?
                options.Value.DefaultConventions : new DocumentConventions();

            _stores = new ConcurrentDictionary<string, Lazy<IDocumentStore>>();
            _servers = options.Value.Servers;
            _host = host;
        }

        /// <summary>
        /// Get a store from the default server.
        /// </summary>
        /// <returns>The store from the default server.</returns>
        public IDocumentStore GetStore()
        {
            if (DefaultServer == null)
                throw new UnknownServerException("There was no default server configured.");

            return GetStore(DefaultServer);
        }

        /// <summary>
        /// Get the store from the specified server.
        /// </summary>
        /// <param name="serverName">The name of the server to get.</param>
        /// <returns>The store from the specified server.</returns>
        public IDocumentStore GetStore(
            string serverName)
        {
            ThrowIfDisposed();

            if (serverName == null)
                throw new ArgumentNullException(nameof(serverName));

            return _stores.GetOrAdd(serverName, CreateDocumentStore(serverName)).Value;
        }


        /// <summary>
        /// Gets a asynchronous session that uses the default server and database.
        /// </summary>
        /// <returns>a asynchronous session for the default server and database.</returns>
        public IAsyncDocumentSession GetAsyncSession()
        {
            if (DefaultServer == null)
                throw new UnknownServerException("There was no default server configured.");

            return GetAsyncSession(new RavenConnection()
            {
                ServerName = DefaultServer
            });
        }

        /// <summary>
        /// Gets a asynchronous session that uses the specified server and database.
        /// </summary>
        /// <param name="connection">The class containing all the information needed to find the correct server and establish the session.</param>
        /// <returns>the specified asynchronous Session.</returns>
        public IAsyncDocumentSession GetAsyncSession(
            RavenConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            var store = GetStore(connection.ServerName);
            if (connection.Database == null)
                return store.OpenAsyncSession();
            else
                return store.OpenAsyncSession(connection.Database);
        }

        /// <summary>
        /// Gets a session that uses the default server and database.
        /// </summary>
        /// <returns>a session for the default server and database.</returns>
        public IDocumentSession GetSession()
        {
            if (DefaultServer == null)
                throw new UnknownServerException("There was no default server configured.");

            return GetSession(new RavenConnection()
            {
                ServerName = DefaultServer
            });
        }

        /// <summary>
        /// Gets a session that uses the specified server and database.
        /// </summary>
        /// <param name="connection">The class containing all the information needed to find the correct server and establish the session.</param>
        /// <returns>The specified Session.</returns>
        public IDocumentSession GetSession(
            RavenConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            var store = GetStore(connection.ServerName);
            if (connection.Database == null)
                return store.OpenSession();
            else
                return store.OpenSession(connection.Database);
        }

        /// <summary>
        /// Create a new document store
        /// </summary>
        /// <param name="serverName">The name of the server which you want to create a document store for.</param>
        /// <returns></returns>
        private Lazy<IDocumentStore> CreateDocumentStore(
            string serverName)
        {
            ThrowIfDisposed();

            if (!_servers.ContainsKey(serverName))
                throw new UnknownServerException("Unable to find specified server: {0}.", serverName);

            var server = _servers[serverName];
            return new Lazy<IDocumentStore>(() =>
            {
                var store = RavenHelpers.CreateDocumentStore(_host, server);
                store.Initialize();
                return store;
            });
        }

        /// <summary>
        /// Add a server to the raven manager.
        /// </summary>
        /// <param name="serverName">The name of the server.</param>
        /// <param name="serverOptions">The options for the server.</param>
        /// <returns>a bool which is true if the server was successfully added.</returns>
        public bool AddServer(
            string serverName, RavenStoreOptions serverOptions)
        {
            ThrowIfDisposed();

            if (serverName == null)
                throw new ArgumentNullException(nameof(serverName));

            if (serverOptions == null)
                throw new ArgumentNullException(nameof(serverOptions));

            return _servers.TryAdd(serverName, serverOptions);
        }

        public IAsyncDocumentSession GetAsyncSession(string serverName)
        {
            if (serverName == null)
                throw new ArgumentNullException(nameof(serverName));

            return GetAsyncSession(new RavenConnection()
            {
                ServerName = serverName
            });
        }

        public IDocumentSession GetSession(string serverName)
        {
            if (serverName == null)
                throw new ArgumentNullException(nameof(serverName));

            return GetSession(new RavenConnection()
            {
                ServerName = serverName
            });
        }

        /// <summary>
        /// Remove a server from the raven manager.
        /// </summary>
        /// <param name="serverName">The name of the server.</param>
        /// <returns>a bool which is true if the server was successfully removed.</returns>
        public bool RemoveServer(string serverName)
        {
            ThrowIfDisposed();

            if (serverName == null)
                throw new ArgumentNullException(nameof(serverName));
			return (_servers.TryRemove(serverName, out _) && _stores.TryRemove(serverName, out _));
        }

        /// <summary>
        /// Throws if this class has been disposed.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Dispose the stores and servers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose the stores and servers.
        /// </summary>
        /// <param name="disposing">Whether the class is actually disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_stores != null)
                    _stores = null;

                if (_servers != null)
                    _servers = null;

                _disposed = true;
            }
        }
    }
}