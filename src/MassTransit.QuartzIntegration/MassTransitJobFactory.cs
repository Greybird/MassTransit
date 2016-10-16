// Copyright 2007-2016 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.QuartzIntegration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq.Expressions;
    using System.Reflection;
    using GreenPipes.Internals.Reflection;
    using Internals.Extensions;
    using Newtonsoft.Json;
    using Quartz;
    using Quartz.Spi;


    public class MassTransitJobFactory :
        IJobFactory
    {
        readonly IBus _bus;
        readonly ConcurrentDictionary<Type, IJobFactory> _typeFactories;

        public MassTransitJobFactory(IBus bus)
        {
            _bus = bus;
            _typeFactories = new ConcurrentDictionary<Type, IJobFactory>();
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            var jobDetail = bundle.JobDetail;
            if (jobDetail == null)
                throw new SchedulerException("JobDetail was null");

            var type = jobDetail.JobType;

            return _typeFactories.GetOrAdd(type, CreateJobFactory)
                .NewJob(bundle, scheduler);
        }

        public void ReturnJob(IJob job)
        {
        }

        IJobFactory CreateJobFactory(Type type)
        {
            var genericType = typeof(MassTransitJobFactory<>).MakeGenericType(type);

            return (IJobFactory)Activator.CreateInstance(genericType, _bus);
        }
    }


    public class MassTransitJobFactory<T> :
        IJobFactory
        where T : IJob
    {
        readonly IBus _bus;
        readonly Func<IBus, T> _factory;
        readonly ReadWritePropertyCache<T> _propertyCache;

        public MassTransitJobFactory(IBus bus)
        {
            _bus = bus;
            _factory = CreateConstructor();

            _propertyCache = new ReadWritePropertyCache<T>(true);
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            try
            {
                var job = _factory(_bus);

                var jobData = new JobDataMap();
                jobData.PutAll(scheduler.Context);
                jobData.PutAll(bundle.JobDetail.JobDataMap);
                jobData.PutAll(bundle.Trigger.JobDataMap);
                jobData.Put("PayloadMessageHeadersAsJson", CreatePayloadHeaderString(bundle));

                SetObjectProperties(job, jobData);

                return job;
            }
            catch (Exception ex)
            {
                var sex = new SchedulerException(string.Format(CultureInfo.InvariantCulture,
                    "Problem instantiating class '{0}'", bundle.JobDetail.JobType.FullName), ex);
                throw sex;
            }
        }

        public void ReturnJob(IJob job)
        {
        }

        void SetObjectProperties(T job, JobDataMap jobData)
        {
            foreach (var key in jobData.Keys)
            {
                ReadWriteProperty<T> property;
                if (_propertyCache.TryGetProperty(key, out property))
                {
                    var value = jobData[key];

                    if (property.Property.PropertyType == typeof(Uri))
                        value = new Uri(value.ToString());

                    property.Set(job, value);
                }
            }
        }

        Func<IBus, T> CreateConstructor()
        {
            var ctor = typeof(T).GetConstructor(new[] {typeof(IBus)});
            if (ctor != null)
                return CreateServiceBusConstructor(ctor);

            ctor = typeof(T).GetConstructor(Type.EmptyTypes);
            if (ctor != null)
                return CreateDefaultConstructor(ctor);

            throw new SchedulerException(string.Format(CultureInfo.InvariantCulture,
                "The job class does not have a supported constructor: {0}", typeof(T).GetTypeName()));
        }

        Func<IBus, T> CreateDefaultConstructor(ConstructorInfo constructorInfo)
        {
            var bus = Expression.Parameter(typeof(IBus), "bus");
            var @new = Expression.New(constructorInfo);

            return Expression.Lambda<Func<IBus, T>>(@new, bus).Compile();
        }

        Func<IBus, T> CreateServiceBusConstructor(ConstructorInfo constructorInfo)
        {
            var bus = Expression.Parameter(typeof(IBus), "bus");
            var @new = Expression.New(constructorInfo, bus);

            return Expression.Lambda<Func<IBus, T>>(@new, bus).Compile();
        }

        /// <summary>
        /// Some additional properties from the TriggerFiredBundle
        /// There is a bug in RabbitMq.Client that prevents using the DateTimeOffset type in the headers
        /// These values are being serialized as ISO-8601 round trip string
        /// </summary>
        /// <param name="bundle"></param>
        string CreatePayloadHeaderString(TriggerFiredBundle bundle)
        {
            var timeHeaders = new Dictionary<string, DateTimeOffset?>();
            if (bundle.ScheduledFireTimeUtc.HasValue)
                timeHeaders.Add(MessageHeaders.Quartz.Scheduled, bundle.ScheduledFireTimeUtc);
            if (bundle.FireTimeUtc.HasValue)
                timeHeaders.Add(MessageHeaders.Quartz.Sent, bundle.FireTimeUtc);
            if (bundle.NextFireTimeUtc.HasValue)
                timeHeaders.Add(MessageHeaders.Quartz.NextScheduled, bundle.NextFireTimeUtc);
            if (bundle.PrevFireTimeUtc.HasValue)
                timeHeaders.Add(MessageHeaders.Quartz.PreviousSent, bundle.PrevFireTimeUtc);

            return JsonConvert.SerializeObject(timeHeaders);
        }
    }
}