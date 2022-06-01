//-----------------------------------------------------------------------
// <copyright file="AttributesExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Supervision;

namespace Akka.Streams.Azure.EventHub
{
    public static class AttributesExtensions
    {
        public static Decider GetDeciderOrDefault(this Attributes attributes)
        {
            var attr = attributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null!);
            return attr != null ? attr.Decider : Deciders.StoppingDecider;
        }
    }
}
