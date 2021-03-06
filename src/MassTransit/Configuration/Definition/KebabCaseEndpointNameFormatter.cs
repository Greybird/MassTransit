// Copyright 2007-2019 Chris Patterson, Dru Sellers, Travis Smith, et. al.
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
namespace MassTransit.Definition
{
    /// <summary>
    /// Formats the endpoint names using kebab-case (dashed snake case)
    ///
    /// SubmitOrderConsumer -> submit-order
    /// OrderState -> order-state
    /// UpdateCustomerActivity -> update-customer-execute, update-customer-compensate
    ///
    /// </summary>
    public class KebabCaseEndpointNameFormatter :
        SnakeCaseEndpointNameFormatter
    {
        public new static IEndpointNameFormatter Instance { get; } = new KebabCaseEndpointNameFormatter();

        public KebabCaseEndpointNameFormatter()
            : base("-")
        {
        }

        protected override string SanitizeName(string name)
        {
            return base.SanitizeName(name).Replace('_', '-');
        }
    }
}
