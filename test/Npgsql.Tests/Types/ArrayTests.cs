﻿#region License
// The PostgreSQL License
//
// Copyright (C) 2015 The Npgsql Development Team
//
// Permission to use, copy, modify, and distribute this software and its
// documentation for any purpose, without fee, and without a written
// agreement is hereby granted, provided that the above copyright notice
// and this paragraph and the following two paragraphs appear in all copies.
//
// IN NO EVENT SHALL THE NPGSQL DEVELOPMENT TEAM BE LIABLE TO ANY PARTY
// FOR DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES,
// INCLUDING LOST PROFITS, ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS
// DOCUMENTATION, EVEN IF THE NPGSQL DEVELOPMENT TEAM HAS BEEN ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.
//
// THE NPGSQL DEVELOPMENT TEAM SPECIFICALLY DISCLAIMS ANY WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY
// AND FITNESS FOR A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS
// ON AN "AS IS" BASIS, AND THE NPGSQL DEVELOPMENT TEAM HAS NO OBLIGATIONS
// TO PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace Npgsql.Tests.Types
{
    /// <summary>
    /// Tests on PostgreSQL arrays
    /// </summary>
    /// <remarks>
    /// http://www.postgresql.org/docs/current/static/arrays.html
    /// </remarks>
    class ArrayTests : TestBase
    {
        public ArrayTests(string backendVersion) : base(backendVersion) {}

        [Test, Description("Roundtrips a simple, one-dimensional array of ints")]
        public void Ints()
        {
            var expected = new[] { 1, 5, 9 };
            var cmd = new NpgsqlCommand("SELECT @p1, @p2", Conn);
            var p1 = new NpgsqlParameter("p1", NpgsqlDbType.Array | NpgsqlDbType.Integer);
            var p2 = new NpgsqlParameter { ParameterName = "p2", Value = expected };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            p1.Value = expected;
            var reader = cmd.ExecuteReader();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetValue(i),                     Is.EqualTo(expected));
                Assert.That(reader.GetProviderSpecificValue(i),     Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<int[]>(i),         Is.EqualTo(expected));
                Assert.That(reader.GetFieldType(i),                 Is.EqualTo(typeof(Array)));
                Assert.That(reader.GetProviderSpecificFieldType(i), Is.EqualTo(typeof(Array)));
            }

            cmd.Dispose();
        }

        [Test, Description("Roundtrips a large, one-dimensional array of ints that will be chunked")]
        public void LongOneDimensional()
        {
            var expected = new int[Conn.BufferSize / 4 + 100];
            for (var i = 0; i < expected.Length; i++)
                expected[i] = i;
            var cmd = new NpgsqlCommand("SELECT @p", Conn);
            var p = new NpgsqlParameter { ParameterName = "p", Value = expected };
            cmd.Parameters.Add(p);
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(expected));
            cmd.Dispose();
        }

        [Test, Description("Roundtrips a large, two-dimensional array of ints that will be chunked")]
        public void LongTwoDimensional()
        {
            var len = Conn.BufferSize / 2 + 100;
            var expected = new int[2, len];
            for (var i = 0; i < len; i++)
                expected[0,i] = i;
            for (var i = 0; i < len; i++)
                expected[1,i] = i;
            var cmd = new NpgsqlCommand("SELECT @p", Conn);
            var p = new NpgsqlParameter { ParameterName = "p", Value = expected };
            cmd.Parameters.Add(p);
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(expected));
            cmd.Dispose();
        }

        [Test, Description("Roundtrips a long, one-dimensional array of strings, including a null")]
        public void StringsWithNull()
        {
            var largeString = new StringBuilder();
            largeString.Append('a', Conn.BufferSize);
            var expected = new[] { "value1", null, largeString.ToString(), "val3" };
            using (var cmd = new NpgsqlCommand("SELECT @p", Conn))
            {
                var p = new NpgsqlParameter("p", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = expected };
                cmd.Parameters.Add(p);
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    Assert.That(reader.GetValue(0), Is.EqualTo(expected));
                    Assert.That(reader.GetProviderSpecificValue(0), Is.EqualTo(expected));
                    Assert.That(reader.GetFieldValue<string[]>(0), Is.EqualTo(expected));
                }
            }
        }

        [Test, Description("Roundtrips a zero-dimensional array of ints, should return empty one-dimensional")]
        public void ZeroDimensional()
        {
            var expected = new int[0];
            var cmd = new NpgsqlCommand("SELECT @p", Conn);
            var p = new NpgsqlParameter("p", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = expected };
            cmd.Parameters.Add(p);
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetValue(0), Is.EqualTo(expected));
            Assert.That(reader.GetProviderSpecificValue(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<int[]>(0), Is.EqualTo(expected));
            cmd.Dispose();
        }

        [Test, Description("Roundtrips a two-dimensional array of ints")]
        public void TwoDimensionalInts()
        {
            var expected = new[,] { { 1, 2, 3 }, { 7, 8, 9 } };
            var cmd = new NpgsqlCommand("SELECT @p1, @p2", Conn);
            var p1 = new NpgsqlParameter("p1", NpgsqlDbType.Array | NpgsqlDbType.Integer);
            var p2 = new NpgsqlParameter { ParameterName = "p2", Value = expected };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            p1.Value = expected;
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetValue(0), Is.EqualTo(expected));
            Assert.That(reader.GetProviderSpecificValue(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<int[,]>(0), Is.EqualTo(expected));
            cmd.Dispose();
        }

        [Test, Description("Reads a one-dimensional array dates, both as DateTime and as the provider-specific NpgsqlDate")]
        public void ReadProviderSpecificType()
        {
            var expectedRegular = new[] { new DateTime(2014, 1, 4),   new DateTime(2014, 1, 8) };
            var expectedPsv     = new[] { new NpgsqlDate(2014, 1, 4), new NpgsqlDate(2014, 1, 8) };
            var cmd = new NpgsqlCommand(@"SELECT '{ ""2014-01-04"", ""2014-01-08"" }'::DATE[]", Conn);
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetValue(0), Is.EqualTo(expectedRegular));
            Assert.That(reader.GetFieldValue<DateTime[]>(0), Is.EqualTo(expectedRegular));
            Assert.That(reader.GetProviderSpecificValue(0), Is.EqualTo(expectedPsv));
            Assert.That(reader.GetFieldValue<NpgsqlDate[]>(0), Is.EqualTo(expectedPsv));
            cmd.Dispose();
        }

        [Test, Description("Reads an one-dimensional array with lower bound != 0")]
        public void ReadNonZeroLowerBounded()
        {
            var cmd = new NpgsqlCommand("SELECT '[2:3]={ 8, 9 }'::INT[]", Conn);
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetFieldValue<int[]>(0), Is.EqualTo(new[] { 8, 9 }));
            reader.Close();
            cmd.Dispose();

            cmd = new NpgsqlCommand("SELECT '[2:3][2:3]={ {8,9}, {1,2} }'::INT[][]", Conn);
            reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetFieldValue<int[,]>(0), Is.EqualTo(new[,] { {8,9}, {1,2} }));
            reader.Close();
            cmd.Dispose();
        }

        [Test, Description("Roundtrips a one-dimensional array of bytea values")]
        public void Byteas()
        {
            var expected = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4, }};
            var cmd = new NpgsqlCommand("SELECT @p1, @p2", Conn);
            var p1 = new NpgsqlParameter("p1", NpgsqlDbType.Array | NpgsqlDbType.Bytea);
            var p2 = new NpgsqlParameter { ParameterName = "p2", Value = expected };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            p1.Value = expected;
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetValue(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<byte[][]>(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(Array)));
            Assert.That(reader.GetProviderSpecificFieldType(0), Is.EqualTo(typeof(Array)));
            cmd.Dispose();
        }

        [Test, Description("Roundtrips a non-generic IList as an array")]
        // ReSharper disable once InconsistentNaming
        public void IListNonGeneric()
        {
            var expected = new ArrayList(new[] { 1, 2, 3 });
            var cmd = new NpgsqlCommand("SELECT @p", Conn);
            var p = new NpgsqlParameter("p", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = expected };
            cmd.Parameters.Add(p);
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(expected.ToArray()));
            cmd.Dispose();
        }

        [Test, Description("Roundtrips a generic IList as an array")]
        // ReSharper disable once InconsistentNaming
        public void IListGeneric()
        {
            var expected = new[] { 1, 2, 3 };
            var cmd = new NpgsqlCommand("SELECT @p1, @p2", Conn);
            var p1 = new NpgsqlParameter { ParameterName = "p1", Value = expected.ToList() };
            var p2 = new NpgsqlParameter { ParameterName = "p2", Value = expected.ToList() };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader[0], Is.EqualTo(expected.ToArray()));
            Assert.That(reader[1], Is.EqualTo(expected.ToArray()));
            cmd.Dispose();
        }
    }
}
