using System;
using System.Threading;
using System.Threading.Tasks;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Results;
using Elsa.Services;
using Elsa.Services.Models;
using MassTransit;
using MT = MassTransit;
using Elsa.Activities.MassTransit.Extensions;
using Elsa.DocumentStore.Persistance;
using Newtonsoft.Json;

namespace Elsa.Activities.MassTransit.Activities
{

    [ActivityDefinition(
       Category = "MassTransit",
       DisplayName = "Send MassTransit Request/Response Message",
       Description = "Send a request/response message via MassTransit."
   )]
    public class RequestMassTransitMessage : MassTransitBusActivity
    {
        private readonly IWorkflowExpressionEvaluator evaluator;
        private readonly IClientFactory _clientFactory;
        private readonly IBus _bus;
        private readonly IServiceProvider serviceProvider;
        private readonly IDocumentStore _documentStore;

        public RequestMassTransitMessage(ConsumeContext consumeContext, IBus bus, IWorkflowExpressionEvaluator evaluator, IClientFactory clientFactory, IServiceProvider serviceProvider, IDocumentStore documentStore = null)
            : base(bus, consumeContext)
        {
            this.evaluator = evaluator;
            this._clientFactory = clientFactory;
            this._bus = bus;
            this.serviceProvider = serviceProvider;
            _documentStore = documentStore;
        }

        [ActivityProperty(Hint = "The assembly-qualified type name of the request message to send.")]
        public Type MessageType
        {
            get
            {
                var typeName = GetState<string>();
                return string.IsNullOrWhiteSpace(typeName) ? null : FindType(typeName);
            }
            set => SetState(value.AssemblyQualifiedName);
        }

        [ActivityProperty(Hint = "The assembly-qualified type name of the response message to receive.")]
        public Type ResponseMessageType
        {
            get
            {
                var typeName = GetState<string>();
                return string.IsNullOrWhiteSpace(typeName) ? null : FindType(typeName);
            }
            set => SetState(value.AssemblyQualifiedName);
        }

        [ActivityProperty(Hint = "An expression that evaluates to the message to send.")]
        public WorkflowExpression Message
        {
            get => GetState<WorkflowExpression>();
            set => SetState(value);
        }


        [ActivityProperty(Hint = "The address of a specific endpoint to send the message to.")]
        public Uri EndpointAddress
        {
            get
            {
                var endpointAddress = GetState<string>();
                return string.IsNullOrEmpty(endpointAddress) ? null : new Uri(endpointAddress);
            }
            set => SetState(value.ToString());
        }

        [ActivityProperty(
            Hint =
                "A value indicating whether the response result is stored into the document store"
        )]
        public bool StoreToDocumentStorage
        {
            get => GetState(() => false);
            set => SetState(value);
        }

        [ActivityProperty(
            Hint =
                "A value indicating which property is stored into the document store. If blank entire message stored."
        )]
        public string MessagePropertyToStore
        {
            get => GetState<string>();
            set => SetState(value);
        }

        protected override bool OnCanExecute(WorkflowExecutionContext context)
        {
            return MessageType != null && ResponseMessageType != null;
        }

        protected override async Task<ActivityExecutionResult> OnExecuteAsync(WorkflowExecutionContext context,
            CancellationToken cancellationToken)
        {
            var message = await evaluator.EvaluateAsync(Message, MessageType, context, cancellationToken);


            var rhType = typeof(IRequestClient<>).MakeGenericType(MessageType);

            RequestHandle rh = null;
            System.Reflection.MethodInfo mi = null;
            System.Reflection.MethodInfo createdMethod = null;

            try
            {
                rh = serviceProvider.GetService(rhType) as RequestHandle;

                if (rh == null)
                {
                    if (this.EndpointAddress == null)
                    {
                        mi = typeof(IClientFactory).GetGenericMethod("CreateRequest", new Type[] { MessageType, typeof(CancellationToken), typeof(RequestTimeout) });
                        createdMethod = mi.MakeGenericMethod(MessageType);
                        rh = (RequestHandle)createdMethod.Invoke(_clientFactory, new object[] { message, cancellationToken, RequestTimeout.After(m: 5) });
                    }
                    else
                    {
                        mi = typeof(IClientFactory).GetGenericMethod("CreateRequest", new Type[] { typeof(Uri), MessageType, typeof(CancellationToken), typeof(RequestTimeout) });
                        createdMethod = mi.MakeGenericMethod(MessageType);
                        rh = (RequestHandle)createdMethod.Invoke(_clientFactory, new object[] { EndpointAddress, message, cancellationToken, RequestTimeout.After(m: 5) });
                    }
                }

                 mi = typeof(RequestHandle).GetGenericMethod("GetResponse", new Type[] { typeof(bool) });

                createdMethod = mi.MakeGenericMethod(ResponseMessageType ?? typeof(object));

                var t = (Task)createdMethod.Invoke(rh, new object[] { true });

                await t.ConfigureAwait(false);

                
                var resultProperty = t.GetType().GetProperty("Result");
                var resultValue = resultProperty.GetValue(t);

                if (resultValue != null)
                {
                    var msgProperty = resultValue.GetType().GetProperty("Message");
                    var msg = msgProperty.GetValue(resultValue);

                    if (StoreToDocumentStorage)
                    {
                        if (_documentStore == null)
                        {
                            throw new Exception("No document storage provider set.");
                        }

                        string docId;
                        if (string.IsNullOrWhiteSpace(MessagePropertyToStore))
                        {
                            docId = await _documentStore.CreateAsync(JsonConvert.SerializeObject(msg)).ConfigureAwait(false);
                        }
                        else
                        {
                            var propToStore = msg.GetType().GetProperty(MessagePropertyToStore);
                            var propToStoreValue = propToStore.GetValue(msg);

                            docId = await _documentStore.CreateAsync(JsonConvert.SerializeObject(propToStoreValue)).ConfigureAwait(false);
                        }
                        
                        context.SetLastResult(new Elsa.Models.Variable(docId));


                    }
                    else
                    {
                        context.SetLastResult(msg);
                    }
                }
                else
                {
                    
                    context.SetLastResult(null);
                }
            }
            finally
            {
                rh?.Dispose();
            }

            

            return Done();
        }



        private static Type FindType(string qualifiedTypeName)
        {
            var t = System.Type.GetType(qualifiedTypeName);

            if (t != null)
            {
                return t;
            }
            else
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(qualifiedTypeName);
                    if (t != null)
                        return t;
                }
                return null;
            }
        }
    }
}
