//-----------------------------------------------------------------------
// <copyright file="Argument.cs" company="Akka.NET Project">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Azure.EventHub
{
    public static class Argument
    {
        public static void AssertNotNull<T>(T value, string name)
        {
            if (value is null)
                throw new ArgumentNullException(name);
        }
        
        public static void AssertInRange<T>(T value, T minimum, T maximum, string name) where T : notnull, IComparable<T>
        {
            if (minimum.CompareTo(value) > 0)
                throw new ArgumentOutOfRangeException(name, "Value is less than the minimum allowed.");

            if (maximum.CompareTo(value) < 0)
                throw new ArgumentOutOfRangeException(name, "Value is greater than the maximum allowed.");
        }
    }
}