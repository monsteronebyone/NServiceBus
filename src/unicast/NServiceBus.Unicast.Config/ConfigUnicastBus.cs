using System;
using System.Configuration;
using Common.Logging;
using NServiceBus.Encryption;
using NServiceBus.ObjectBuilder;
using System.Collections;
using System.Reflection;
using NServiceBus.Config;
using System.Collections.Generic;
using NServiceBus.Saga;
using System.Linq;

namespace NServiceBus.Unicast.Config
{
    /// <summary>
    /// Inherits NServiceBus.Configure providing UnicastBus specific configuration on top of it.
    /// </summary>
    public class ConfigUnicastBus : Configure
    {
        /// <summary>
        /// A map of which message types (belonging to the given assemblies) are owned 
        /// by which endpoint.
        /// </summary>
        protected Hashtable assembliesToEndpoints = new Hashtable();

        /// <summary>
        /// Wrap the given configure object storing its builder and configurer.
        /// </summary>
        /// <param name="config"></param>
        public void Configure(Configure config)
        {
            Builder = config.Builder;
            Configurer = config.Configurer;

            busConfig = Configurer.ConfigureComponent<UnicastBus>(ComponentCallModelEnum.Singleton);

            ConfigureSubscriptionAuthorization();

            RegisterMessageModules();

            RegisterEncryptionMutator();

            var c = GetConfigSection<MsmqTransportConfig>();
            if (c != null)
                busConfig.ConfigureProperty(t => t.Address, c.InputQueue);

            var cfg = GetConfigSection<UnicastBusConfig>();

            if (cfg != null)
            {
                TypesToScan.Where(t => typeof(IMessage).IsAssignableFrom(t)).ToList().ForEach(
                        t => assembliesToEndpoints[t.Assembly.GetName().Name] = string.Empty                   
                    );

                foreach (MessageEndpointMapping mapping in cfg.MessageEndpointMappings)
                    assembliesToEndpoints[mapping.Messages] = mapping.Endpoint;

                busConfig.ConfigureProperty(b => b.ForwardReceivedMessagesTo, cfg.ForwardReceivedMessagesTo);
                busConfig.ConfigureProperty(b => b.MessageOwners, assembliesToEndpoints);
            }
        }

        void RegisterEncryptionMutator()
        {
            Configurer.ConfigureComponent<EncryptionMessageMutator>(ComponentCallModelEnum.Singlecall);
        }

        private void RegisterMessageModules()
        {
            TypesToScan.Where(t => typeof(IMessageModule).IsAssignableFrom(t) && !t.IsInterface).ToList().ForEach(
                type => Configurer.ConfigureComponent(type, ComponentCallModelEnum.Singlecall)
                );
        }

        private void ConfigureSubscriptionAuthorization()
        {
            Type authType =
                TypesToScan.Where(t => typeof (IAuthorizeSubscriptions).IsAssignableFrom(t) && !t.IsInterface).
                    FirstOrDefault();

            if (authType != null)
                Configurer.ConfigureComponent(authType, ComponentCallModelEnum.Singleton);
        }

        /// <summary>
        /// Used to configure the bus.
        /// </summary>
        protected IComponentConfig<UnicastBus> busConfig;

        /// <summary>
        /// Loads all message handler assemblies in the runtime directory.
        /// </summary>
        /// <returns></returns>
        public ConfigUnicastBus LoadMessageHandlers()
        {
            var types = new List<Type>();

            TypesToScan.Where(TypeSpecifiesMessageHandlerOrdering)
                .ToList().ForEach(t =>
                {
                    Logger.DebugFormat("Going to ask for message handler ordering from {0}.", t);

                    var order = new Order();
                    ((ISpecifyMessageHandlerOrdering) Activator.CreateInstance(t)).SpecifyOrder(order);

                    order.Types.ToList().ForEach(ht =>
                                      {
                                          if (types.Contains(ht))
                                              throw new ConfigurationErrorsException(string.Format("The order in which the type {0} should be invoked was already specified by a previous implementor of ISpecifyMessageHandlerOrdering. Check the debug logs to see which other specifiers have been invoked.", ht));
                                      });

                    types.AddRange(order.Types);
                });

            return LoadMessageHandlers(types);
        }

        /// <summary>
        /// Loads all message handler assemblies in the runtime directory
        /// and specifies that handlers in the given assembly should run
        /// before all others.
        /// 
        /// Use First{T} to indicate the type to load from.
        /// </summary>
        /// <typeparam name="TFirst"></typeparam>
        /// <returns></returns>
        public ConfigUnicastBus LoadMessageHandlers<TFirst>()
        {
            Type[] args = typeof(TFirst).GetGenericArguments();
            if (args.Length == 1)
            {
                if (typeof(First<>).MakeGenericType(args[0]).IsAssignableFrom(typeof(TFirst)))
                {
                    return LoadMessageHandlers(new[] {args[0]});
                }
            }

            throw new ArgumentException("TFirst should be of the type First<T> where T is the type to indicate as first.");
        }

        /// <summary>
        /// Loads all message handler assemblies in the runtime directory
        /// and specifies that the handlers in the given 'order' are to 
        /// run before all others and in the order specified.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="order"></param>
        /// <returns></returns>
        public ConfigUnicastBus LoadMessageHandlers<T>(First<T> order)
        {
            return LoadMessageHandlers(order.Types);
        }

        private ConfigUnicastBus LoadMessageHandlers(IEnumerable<Type> orderedTypes)
        {
            LoadMessageHandlersCalled = true;
            var types = new List<Type>(TypesToScan);

            foreach (Type t in orderedTypes)
                types.Remove(t);

            types.InsertRange(0, orderedTypes);

            return ConfigureMessageHandlersIn(types);
        }

        /// <summary>
        /// Scans the given types for types that are message handlers
        /// then uses the Configurer to configure them into the container as single call components,
        /// finally passing them to the bus as its MessageHandlerTypes.
        /// </summary>
        /// <param name="types"></param>
        /// <returns></returns>
        protected ConfigUnicastBus ConfigureMessageHandlersIn(IEnumerable<Type> types)
        {
            var handlers = new List<Type>();

            foreach (Type t in types.Where(IsMessageHandler))
            {
                Configurer.ConfigureComponent(t, ComponentCallModelEnum.Singlecall);
                handlers.Add(t);
            }

            busConfig.ConfigureProperty(b => b.MessageHandlerTypes, handlers);

            return this;
        }

        /// <summary>
        /// Set this if you want this endpoint to serve as something of a proxy;
        /// recipients of messages sent by this endpoint will see the address
        /// of endpoints that sent the incoming messages.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ConfigUnicastBus PropogateReturnAddressOnSend(bool value)
        {
            busConfig.ConfigureProperty(b => b.PropogateReturnAddressOnSend, value);
            return this;
        }

        /// <summary>
        /// Forwards all received messages to a given endpoint (queue@machine).
        /// This is useful as an auditing/debugging mechanism.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ConfigUnicastBus ForwardReceivedMessagesTo(string  value)
        {
            busConfig.ConfigureProperty(b => b.ForwardReceivedMessagesTo, value);
            return this;
        }

        /// <summary>
        /// Instructs the bus not to automatically subscribe to messages that
        /// it has handlers for (given those messages belong to a different endpoint).
        /// 
        /// This is needed only if you require fine-grained control over the subscribe/unsubscribe process.
        /// </summary>
        /// <returns></returns>
        public ConfigUnicastBus DoNotAutoSubscribe()
        {
            busConfig.ConfigureProperty(b => b.AutoSubscribe, false);
            return this;
        }

        /// <summary>
        /// Returns true if the given type is a message handler.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool IsMessageHandler(Type t)
        {
            if (t.IsAbstract)
                return false;

            if (typeof(ISaga).IsAssignableFrom(t))
                return false;

            foreach (Type interfaceType in t.GetInterfaces())
            {
                Type messageType = GetMessageTypeFromMessageHandler(interfaceType);
                if (messageType != null)
                    return true;
            }

            return false;
        }

        private static bool TypeSpecifiesMessageHandlerOrdering(Type t)
        {
            return typeof (ISpecifyMessageHandlerOrdering).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface;
        }

        /// <summary>
        /// Returns the message type handled by the given message handler type.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static Type GetMessageTypeFromMessageHandler(Type t)
        {
            if (t.IsGenericType)
            {
                Type[] args = t.GetGenericArguments();
                if (args.Length != 1)
                    return null;

                if (!typeof(IMessage).IsAssignableFrom(args[0]))
                    return null;

                Type handlerType = typeof(IMessageHandler<>).MakeGenericType(args[0]);
                if (handlerType.IsAssignableFrom(t))
                    return args[0];
            }

            return null;
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof (UnicastBus));

        internal bool LoadMessageHandlersCalled { get; private set; }

    }
}
