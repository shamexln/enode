﻿using System.Linq;
using System.Threading;
using ECommon.IoC;
using ECommon.Scheduling;
using ECommon.Utilities;
using ENode.Commanding;
using ENode.Configurations;
using ENode.EQueue;
using ENode.EQueue.Commanding;
using EQueue.Clients.Consumers;
using EQueue.Configurations;

namespace DistributeSample.CommandProducer.EQueueIntegrations
{
    public static class ENodeExtensions
    {
        private static CommandService _commandService;
        private static CommandResultProcessor _commandResultProcessor;

        public static ENodeConfiguration UseEQueue(this ENodeConfiguration enodeConfiguration)
        {
            var configuration = enodeConfiguration.GetCommonConfiguration();

            configuration.RegisterEQueueComponents();
            configuration.SetDefault<ICommandTopicProvider, CommandTopicManager>();
            configuration.SetDefault<ICommandTypeCodeProvider, CommandTypeCodeManager>();

            var consumerSetting = new ConsumerSetting
            {
                HeartbeatBrokerInterval = 1000,
                UpdateTopicQueueCountInterval = 1000,
                RebalanceInterval = 1000
            };

            var failedCommandMessageConsumer = new Consumer(consumerSetting, "CommandResultProcessor_FailedCommandMessageConsumer", "FailedCommandMessageConsumerGroup_" + ObjectId.GenerateNewId().ToString());
            var domainEventHandledMessageConsumer = new Consumer(consumerSetting, "CommandResultProcessor_DomainEventHandledMessageConsumer", "DomainEventHandledMessageConsumerGroup_" + ObjectId.GenerateNewId().ToString());
            _commandResultProcessor = new CommandResultProcessor(failedCommandMessageConsumer, domainEventHandledMessageConsumer);

            _commandService = new CommandService(_commandResultProcessor);

            configuration.SetDefault<ICommandService, CommandService>(_commandService);

            _commandResultProcessor.SetFailedCommandMessageTopic("FailedCommandMessageTopic");
            _commandResultProcessor.SetDomainEventHandledMessageTopic("DomainEventHandledMessageTopic");

            return enodeConfiguration;
        }
        public static ENodeConfiguration StartEQueue(this ENodeConfiguration enodeConfiguration)
        {
            _commandService.Start();
            _commandResultProcessor.Start();

            WaitAllConsumerLoadBalanceComplete();

            return enodeConfiguration;
        }

        private static void WaitAllConsumerLoadBalanceComplete()
        {
            var scheduleService = ObjectContainer.Resolve<IScheduleService>();
            var waitHandle = new ManualResetEvent(false);
            var taskId = scheduleService.ScheduleTask(() =>
            {
                var failedCommandMessageConsumerAllocatedQueues = _commandResultProcessor.FailedCommandMessageConsumer.GetCurrentQueues();
                var domainEventHandledMessageConsumerAllocatedQueues = _commandResultProcessor.DomainEventHandledMessageConsumer.GetCurrentQueues();
                if (failedCommandMessageConsumerAllocatedQueues.Count() == 4 && domainEventHandledMessageConsumerAllocatedQueues.Count() == 4)
                {
                    waitHandle.Set();
                }
            }, 1000, 1000);

            waitHandle.WaitOne();
            scheduleService.ShutdownTask(taskId);
        }
    }
}
