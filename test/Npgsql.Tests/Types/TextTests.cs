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
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;

namespace Npgsql.Tests.Types
{
    /// <summary>
    /// Tests on PostgreSQL text
    /// </summary>
    /// <remarks>
    /// http://www.postgresql.org/docs/current/static/datatype-character.html
    /// </remarks>
    public class TextTests : TestBase
    {
        [Test, Description("Roundtrips a string")]
        public void Roundtrip()
        {
            const string expected = "Something";
            var cmd = new NpgsqlCommand("SELECT @p1, @p2, @p3, @p4", Conn);
            var p1 = new NpgsqlParameter("p1", NpgsqlDbType.Text);
            var p2 = new NpgsqlParameter("p2", NpgsqlDbType.Varchar);
            var p3 = new NpgsqlParameter("p3", DbType.String);
            var p4 = new NpgsqlParameter { ParameterName = "p4", Value = expected };
            Assert.That(p2.DbType, Is.EqualTo(DbType.String));
            Assert.That(p3.NpgsqlDbType, Is.EqualTo(NpgsqlDbType.Text));
            Assert.That(p3.DbType, Is.EqualTo(DbType.String));
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            cmd.Parameters.Add(p3);
            cmd.Parameters.Add(p4);
            p1.Value = p2.Value = p3.Value = expected;
            var reader = cmd.ExecuteReader();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetFieldType(i),          Is.EqualTo(typeof(string)));
                Assert.That(reader.GetString(i),             Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<string>(i), Is.EqualTo(expected));
                Assert.That(reader.GetValue(i),              Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<char[]>(i), Is.EqualTo(expected.ToCharArray()));
            }

            reader.Close();
            cmd.Dispose();
        }

        [Test]
        public void Long([Values(CommandBehavior.Default, CommandBehavior.SequentialAccess)] CommandBehavior behavior)
        {
            var builder = new StringBuilder("ABCDEééé", Conn.BufferSize);
            builder.Append('X', Conn.BufferSize);
            var expected = builder.ToString();
            ExecuteNonQuery("CREATE TEMP TABLE data (name TEXT)");
            var cmd = new NpgsqlCommand(@"INSERT INTO data (name) VALUES (@p)", Conn);
            cmd.Parameters.Add(new NpgsqlParameter("p", expected));
            cmd.ExecuteNonQuery();

            const string queryText = @"SELECT name, 'foo', name, name, name, name FROM data";
            cmd = new NpgsqlCommand(queryText, Conn);
            var reader = cmd.ExecuteReader(behavior);
            reader.Read();

            var actual = reader[0];
            Assert.That(actual, Is.EqualTo(expected));

            if (IsSequential(behavior))
                Assert.That(() => reader[0], Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
            else
                Assert.That(reader[0], Is.EqualTo(expected));

            Assert.That(reader.GetString(1), Is.EqualTo("foo"));
            Assert.That(reader.GetFieldValue<string>(2), Is.EqualTo(expected));
            Assert.That(reader.GetValue(3), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<string>(4), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<char[]>(5), Is.EqualTo(expected.ToCharArray()));
        }

        [Test]
        public void GetChars([Values(CommandBehavior.Default, CommandBehavior.SequentialAccess)] CommandBehavior behavior)
        {
            // TODO: This is too small to actually test any interesting sequential behavior
            const string str = "ABCDE";
            var expected = str.ToCharArray();
            var actual = new char[expected.Length];
            ExecuteNonQuery("CREATE TEMP TABLE data (name TEXT)");
            ExecuteNonQuery(String.Format(@"INSERT INTO data (name) VALUES ('{0}')", str));

            const string queryText = @"SELECT name, 3, name, 4, name, name, name FROM data";
            var cmd = new NpgsqlCommand(queryText, Conn);
            var reader = cmd.ExecuteReader(behavior);
            reader.Read();

            Assert.That(reader.GetChars(0, 0, actual, 0, 2), Is.EqualTo(2));
            Assert.That(actual[0], Is.EqualTo(expected[0]));
            Assert.That(actual[1], Is.EqualTo(expected[1]));
            Assert.That(reader.GetChars(0, 0, null, 0, 0), Is.EqualTo(expected.Length), "Bad column length");
            // Note: Unlike with bytea, finding out the length of the column consumes it (variable-width
            // UTF8 encoding)
            Assert.That(reader.GetChars(2, 0, actual, 0, 2), Is.EqualTo(2));
            if (IsSequential(behavior))
                Assert.That(() => reader.GetChars(2, 0, actual, 4, 1), Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
            else
            {
                Assert.That(reader.GetChars(2, 0, actual, 4, 1), Is.EqualTo(1));
                Assert.That(actual[4], Is.EqualTo(expected[0]));
            }
            Assert.That(reader.GetChars(2, 2, actual, 2, 3), Is.EqualTo(3));
            Assert.That(actual, Is.EqualTo(expected));
            //Assert.That(reader.GetChars(2, 0, null, 0, 0), Is.EqualTo(expected.Length), "Bad column length");

            Assert.That(() => reader.GetChars(3, 0, null, 0, 0), Throws.Exception.TypeOf<InvalidCastException>(), "GetChars on non-text");
            Assert.That(() => reader.GetChars(3, 0, actual, 0, 1), Throws.Exception.TypeOf<InvalidCastException>(), "GetChars on non-text");
            Assert.That(reader.GetInt32(3), Is.EqualTo(4));
            reader.GetChars(4, 0, actual, 0, 2);
            // Jump to another column from the middle of the column
            reader.GetChars(5, 0, actual, 0, 2);
            Assert.That(reader.GetChars(5, expected.Length - 1, actual, 0, 2), Is.EqualTo(1), "Length greater than data length");
            Assert.That(actual[0], Is.EqualTo(expected[expected.Length - 1]), "Length greater than data length");
            Assert.That(() => reader.GetChars(5, 0, actual, 0, actual.Length + 1), Throws.Exception.TypeOf<IndexOutOfRangeException>(), "Length great than output buffer length");
            // Close in the middle of a column
            reader.GetChars(6, 0, actual, 0, 2);
            reader.Close();
            cmd.Dispose();
        }

        [Test]
        public void GetTextReader([Values(CommandBehavior.Default, CommandBehavior.SequentialAccess)] CommandBehavior behavior)
        {
            // TODO: This is too small to actually test any interesting sequential behavior
            const string str = "ABCDE";
            var expected = str.ToCharArray();
            var actual = new char[expected.Length];
            //ExecuteNonQuery(String.Format(@"INSERT INTO data (field_text) VALUES ('{0}')", str));

            string queryText = string.Format(@"SELECT '{0}', 'foo'", str);
            var cmd = new NpgsqlCommand(queryText, Conn);
            var reader = cmd.ExecuteReader(behavior);
            reader.Read();

            var textReader = reader.GetTextReader(0);
            textReader.Read(actual, 0, 2);
            Assert.That(actual[0], Is.EqualTo(expected[0]));
            Assert.That(actual[1], Is.EqualTo(expected[1]));
            if (behavior == CommandBehavior.Default) {
                var textReader2 = reader.GetTextReader(0);
                var actual2 = new char[2];
                textReader2.Read(actual2, 0, 2);
                Assert.That(actual2[0], Is.EqualTo(expected[0]));
                Assert.That(actual2[1], Is.EqualTo(expected[1]));
            } else {
                Assert.That(() => reader.GetTextReader(0), Throws.Exception.TypeOf<InvalidOperationException>(), "Sequential text reader twice on same column");
            }
            textReader.Read(actual, 2, 1);
            Assert.That(actual[2], Is.EqualTo(expected[2]));
            textReader.Close();

            if (IsSequential(behavior))
                Assert.That(() => reader.GetChars(0, 0, actual, 4, 1), Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
            else
            {
                Assert.That(reader.GetChars(0, 0, actual, 4, 1), Is.EqualTo(1));
                Assert.That(actual[4], Is.EqualTo(expected[0]));
            }
            Assert.That(reader.GetString(1), Is.EqualTo("foo"));
            reader.Close();
            cmd.Dispose();
        }

        [Test, Description("In sequential mode, checks that moving to the next column disposes a currently open text reader")]
        public void TextReaderDisposeOnSequentialColumn()
        {
            var cmd = new NpgsqlCommand(@"SELECT 'some_text', 'some_text'", Conn);
            var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
            reader.Read();
            var textReader = reader.GetTextReader(0);
            // ReSharper disable once UnusedVariable
            var v = reader.GetValue(1);
            Assert.That(() => textReader.Peek(), Throws.Exception.TypeOf<ObjectDisposedException>());
            reader.Close();
            cmd.Dispose();
        }

        [Test, Description("In non-sequential mode, checks that moving to the next row disposes all currently open text readers")]
        public void TextReaderDisposeOnNonSequentialRow()
        {
            var cmd = new NpgsqlCommand(@"SELECT 'some_text', 'some_text'", Conn);
            var reader = cmd.ExecuteReader();
            reader.Read();
            var tr1 = reader.GetTextReader(0);
            var tr2 = reader.GetTextReader(0);
            reader.Read();
            Assert.That(() => tr1.Peek(), Throws.Exception.TypeOf<ObjectDisposedException>());
            Assert.That(() => tr2.Peek(), Throws.Exception.TypeOf<ObjectDisposedException>());
            reader.Close();
            cmd.Dispose();
        }

        [Test]
        public void Null([Values(CommandBehavior.Default, CommandBehavior.SequentialAccess)] CommandBehavior behavior)
        {
            var buf = new char[8];
            var cmd = new NpgsqlCommand("SELECT NULL::TEXT", Conn);
            var reader = cmd.ExecuteReader(behavior);
            reader.Read();
            Assert.That(reader.IsDBNull(0), Is.True);
            Assert.That(() => reader.GetChars(0, 0, buf, 0, 1), Throws.Exception, "GetChars");
            Assert.That(() => reader.GetTextReader(0), Throws.Exception, "GetTextReader");
            Assert.That(() => reader.GetChars(0, 0, null, 0, 0), Throws.Exception, "GetChars with null buffer");
            reader.Close();
            cmd.Dispose();
        }

        [Test, Description("Tests that strings are truncated when the NpgsqlParameter's Size is set")]
        public void Truncate()
        {
            const string data = "SomeText";
            var cmd = new NpgsqlCommand("SELECT @p::TEXT", Conn);
            var p = new NpgsqlParameter("p", data) { Size = 4 };
            cmd.Parameters.Add(p);
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(data.Substring(0, 4)));

            // NpgsqlParameter.Size needs to persist when value is changed
            const string data2 = "AnotherValue";
            p.Value = data2;
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(data2.Substring(0, 4)));

            // NpgsqlParameter.Size larger than the value size should mean the value size, as well as 0 and -1
            p.Size = data2.Length + 10;
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(data2));
            p.Size = 0;
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(data2));
            p.Size = -1;
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(data2));

            Assert.That(() => p.Size = -2, Throws.Exception.TypeOf<ArgumentException>());
        }

        [Test]
        [IssueLink("https://github.com/npgsql/npgsql/issues/488")]
        public void NullCharacter()
        {
            var cmd = new NpgsqlCommand("SELECT @p1", Conn);
            cmd.Parameters.Add(new NpgsqlParameter("p1", "string with \0\0\0 null \0bytes"));
            Assert.That(() => cmd.ExecuteReader(),
                Throws.Exception.TypeOf<NpgsqlException>()
                .With.Property("Code").EqualTo("22021")
            );
        }

        [Test, Description("Tests some types which are aliased to strings")]
        [TestCase("varchar")]
        [TestCase("name")]
        public void AliasedPgTypes(string typename)
        {
            const string expected = "some_text";
            var cmd = new NpgsqlCommand(String.Format("SELECT '{0}'::{1}", expected, typename), Conn);
            var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetString(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<char[]>(0), Is.EqualTo(expected.ToCharArray()));
            reader.Dispose();
            cmd.Dispose();
        }


        [Test]
        [TestCase(DbType.AnsiString)]
        [TestCase(DbType.AnsiStringFixedLength)]
        public void AliasedDbTypes(DbType dbType)
        {
            using (var command = new NpgsqlCommand("SELECT @p", Conn))
            {
                command.Parameters.Add(new NpgsqlParameter("p", dbType) { Value = "SomeString" });
                Assert.That(command.ExecuteScalar(), Is.EqualTo("SomeString"));
            }
        }

        [Test, Description("Tests the PostgreSQL internal \"char\" type")]
        public void InternalChar([Values(true, false)] bool prepareCommand)
        {
            using (var cmd = Conn.CreateCommand()) {
                var testArr = new byte[] { prepareCommand ? (byte)200 : (byte)'}', prepareCommand ? (byte)0 : (byte)'"', 3 };
                var testArr2 = new char[] { prepareCommand ? (char)200 : '}', prepareCommand ? (char)0 : '"', (char)3 };

                cmd.CommandText = "Select 'a'::\"char\", (-3)::\"char\", :p1, :p2, :p3, :p4, :p5";
                cmd.Parameters.Add(new NpgsqlParameter("p1", NpgsqlDbType.InternalChar) { Value = 'b' });
                cmd.Parameters.Add(new NpgsqlParameter("p2", NpgsqlDbType.InternalChar) { Value = (byte)66 });
                cmd.Parameters.Add(new NpgsqlParameter("p3", NpgsqlDbType.InternalChar) { Value = (byte)230 });
                cmd.Parameters.Add(new NpgsqlParameter("p4", NpgsqlDbType.InternalChar | NpgsqlDbType.Array) { Value = testArr });
                cmd.Parameters.Add(new NpgsqlParameter("p5", NpgsqlDbType.InternalChar | NpgsqlDbType.Array) { Value = testArr2 });
                if (prepareCommand)
                    cmd.Prepare();
                using (var reader = cmd.ExecuteReader()) {
                    reader.Read();
                    var expected = new char[] { 'a', (char)(256 - 3), 'b', (char)66, (char)230 };
                    for (int i = 0; i < expected.Length; i++) {
                        Assert.AreEqual(expected[i], reader.GetChar(i));
                    }
                    var arr = (char[])reader.GetValue(5);
                    var arr2 = (char[])reader.GetValue(6);
                    Assert.AreEqual(testArr.Length, arr.Length);
                    for (int i = 0; i < arr.Length; i++) {
                        Assert.AreEqual(testArr[i], arr[i]);
                        Assert.AreEqual(testArr2[i], arr2[i]);
                    }
                }
            }
        }

        [Test]
        public void Char()
        {
            var expected = 'f';
            using (var cmd = new NpgsqlCommand("SELECT @p", Conn))
            {
                cmd.Parameters.AddWithValue("p", expected);
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    Assert.That(reader.GetString(0), Is.EqualTo(expected.ToString()));
                    Assert.That(() => reader.GetChar(0), Throws.Exception);
                }
            }
        }

        [Test, Description("Checks support for the citext contrib type")]
        [IssueLink("https://github.com/npgsql/npgsql/issues/695")]
        public void Citext()
        {
            if (Conn.PostgreSqlVersion >= new Version(9, 1, 0))
            {
                ExecuteNonQuery("CREATE EXTENSION IF NOT EXISTS citext");
                TypeHandlerRegistry.ClearBackendTypeCache();
            }

            using (var conn = new NpgsqlConnection(ConnectionString + ";Pooling=false"))
            {
                conn.Open();
                var value = "Foo";
                using (var cmd = new NpgsqlCommand("SELECT @p::CITEXT", conn))
                {
                    cmd.Parameters.AddWithValue("p", value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        reader.Read();
                        Assert.That(reader.GetString(0), Is.EqualTo(value));
                    }
                }
            }
        }

        public TextTests(string backendVersion) : base(backendVersion) { }
    }
}
