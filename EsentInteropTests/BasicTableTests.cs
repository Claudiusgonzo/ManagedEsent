﻿//-----------------------------------------------------------------------
// <copyright file="BasicTableTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.
// </copyright>
//-----------------------------------------------------------------------

namespace InteropApiTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using Microsoft.Isam.Esent.Interop;
    using Microsoft.Isam.Esent.Interop.Vista;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Basic Api tests
    /// </summary>
    [TestClass]
    public class BasicTableTests
    {
        /// <summary>
        /// The directory being used for the database and its files.
        /// </summary>
        private string directory;

        /// <summary>
        /// The path to the database being used by the test.
        /// </summary>
        private string database;

        /// <summary>
        /// The name of the table.
        /// </summary>
        private string table;

        /// <summary>
        /// The instance used by the test.
        /// </summary>
        private JET_INSTANCE instance;

        /// <summary>
        /// The session used by the test.
        /// </summary>
        private JET_SESID sesid;

        /// <summary>
        /// Identifies the database used by the test.
        /// </summary>
        private JET_DBID dbid;

        /// <summary>
        /// The tableid being used by the test.
        /// </summary>
        private JET_TABLEID tableid;

        /// <summary>
        /// Columnid of the LongText column in the table.
        /// </summary>
        private JET_COLUMNID columnidLongText;

        #region Setup/Teardown

        /// <summary>
        /// Initialization method. Called once when the tests are started.
        /// All DDL should be done in this method.
        /// </summary>
        [TestInitialize]
        [Description("Setup for BasicTableTests.")]
        public void Setup()
        {
            this.directory = SetupHelper.CreateRandomDirectory();
            this.database = Path.Combine(this.directory, "database.edb");
            this.table = "table";
            this.instance = SetupHelper.CreateNewInstance(this.directory);

            Api.JetSetSystemParameter(this.instance, JET_SESID.Nil, JET_param.PageTempDBMin, SystemParameters.PageTempDBSmallest, null);
            Api.JetInit(ref this.instance);
            Api.JetBeginSession(this.instance, out this.sesid, String.Empty, String.Empty);
            Api.JetCreateDatabase(this.sesid, this.database, String.Empty, out this.dbid, CreateDatabaseGrbit.None);
            Api.JetBeginTransaction(this.sesid);
            Api.JetCreateTable(this.sesid, this.dbid, this.table, 0, 100, out this.tableid);

            var columndef = new JET_COLUMNDEF()
            {
                cp = JET_CP.Unicode,
                coltyp = JET_coltyp.LongText,
            };
            Api.JetAddColumn(this.sesid, this.tableid, "TestColumn", columndef, null, 0, out this.columnidLongText);

            Api.JetCloseTable(this.sesid, this.tableid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);
            Api.JetOpenTable(this.sesid, this.dbid, this.table, null, 0, OpenTableGrbit.None, out this.tableid);
        }

        /// <summary>
        /// Cleanup after all tests have run.
        /// </summary>
        [TestCleanup]
        [Description("Cleanup for BasicTableTests.")]
        public void Teardown()
        {
            Api.JetCloseTable(this.sesid, this.tableid);
            Api.JetEndSession(this.sesid, EndSessionGrbit.None);
            Api.JetTerm(this.instance);
            Cleanup.DeleteDirectoryWithRetry(this.directory);
        }

        /// <summary>
        /// Verify that BasicTableTests has setup the test fixture properly.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Verify that BasicTableTests has setup the test fixture properly.")]
        public void VerifyFixtureSetup()
        {
            Assert.IsNotNull(this.table);
            Assert.AreNotEqual(JET_INSTANCE.Nil, this.instance);
            Assert.AreNotEqual(JET_SESID.Nil, this.sesid);
            Assert.AreNotEqual(JET_DBID.Nil, this.dbid);
            Assert.AreNotEqual(JET_TABLEID.Nil, this.tableid);
            Assert.AreNotEqual(JET_COLUMNID.Nil, this.columnidLongText);

            JET_COLUMNDEF columndef;
            Api.JetGetTableColumnInfo(this.sesid, this.tableid, this.columnidLongText, out columndef);
            Assert.AreEqual(JET_coltyp.LongText, columndef.coltyp);
        }

        #endregion Setup/Teardown

        #region Session tests

        /// <summary>
        /// Test moving a transaction between threads.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Test moving a transaction between threads.")]
        public void VerifyJetSetSessionContextAllowsThreadMigration()
        {
            // Without the calls to JetSetSessionContext/JetResetSessionContext
            // this will generate a session sharing violation.
            var context = new IntPtr(Any.Int32);

            var thread = new Thread(() =>
                {
                    Api.JetSetSessionContext(this.sesid, context);
                    Api.JetBeginTransaction(this.sesid);
                    Api.JetResetSessionContext(this.sesid);
                });
            thread.Start();
            thread.Join();

            Api.JetSetSessionContext(this.sesid, context);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.None);
            Api.JetResetSessionContext(this.sesid);
        }

        #endregion

        #region JetDupCursor

        /// <summary>
        /// Verify JetDupCursor returns a different tableid.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Verify JetDupCursor returns a different tableid.")]
        public void VerifyJetDupCursorReturnsDifferentTableid()
        {
            JET_TABLEID newTableid;
            Api.JetDupCursor(this.sesid, this.tableid, out newTableid, DupCursorGrbit.None);
            Assert.AreNotEqual(newTableid, this.tableid);
            Api.JetCloseTable(this.sesid, newTableid);
        }

        /// <summary>
        /// Verify JetDupCursor should returns a tableid on the same table.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Verify JetDupCursor should returns a tableid on the same table.")]
        public void VerifyJetDupCursorReturnsCursorOnSameTable()
        {
            string expected = Any.String;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(expected);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            JET_TABLEID newTableid;
            Api.JetDupCursor(this.sesid, this.tableid, out newTableid, DupCursorGrbit.None);
            Api.JetMove(this.sesid, newTableid, JET_Move.First, MoveGrbit.None);
            string actual = Api.RetrieveColumnAsString(this.sesid, newTableid, this.columnidLongText, Encoding.Unicode);
            Assert.AreEqual(expected, actual);
            Api.JetCloseTable(this.sesid, newTableid);
        }

        #endregion JetDupCursor

        #region DDL Tests

        /// <summary>
        /// Create one column of each type.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Create one column of each type.")]
        public void CreateOneColumnOfEachType()
        {
            Api.JetBeginTransaction(this.sesid);
            foreach (JET_coltyp coltyp in Enum.GetValues(typeof(JET_coltyp)))
            {
                if (JET_coltyp.Nil != coltyp)
                {
                    var columndef = new JET_COLUMNDEF { coltyp = coltyp };
                    JET_COLUMNID columnid;
                    Api.JetAddColumn(this.sesid, this.tableid, coltyp.ToString(), columndef, null, 0, out columnid);
                    Assert.AreEqual(columnid, columndef.columnid);
                }
            }

            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);
        }

        /// <summary>
        /// Create a column with a default value.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Create a column with a default value.")]
        public void CreateColumnWithDefaultValue()
        {
            int expected = Any.Int32;

            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF { coltyp = JET_coltyp.Long };
            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, "column_with_default", columndef, BitConverter.GetBytes(expected), 4, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Assert.AreEqual(expected, Api.RetrieveColumnAsInt32(this.sesid, this.tableid, columnid));
        }

        /// <summary>
        /// Add a column and retrieve its information using JetGetTableColumnInfo.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Add a column and retrieve its information using JetGetTableColumnInfo.")]
        public void JetGetTableColumnInfo()
        {
            const string ColumnName = "column1";
            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF()
            {
                cbMax = 4096,
                cp = JET_CP.Unicode,
                coltyp = JET_coltyp.LongText,
                grbit = ColumndefGrbit.None,                
            };
            
            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, ColumnName, columndef, null, 0, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            JET_COLUMNDEF retrievedColumndef;
            Api.JetGetTableColumnInfo(this.sesid, this.tableid, ColumnName, out retrievedColumndef);

            Assert.AreEqual(columndef.cbMax, retrievedColumndef.cbMax);
            Assert.AreEqual(columndef.cp, retrievedColumndef.cp);
            Assert.AreEqual(columndef.coltyp, retrievedColumndef.coltyp);
            Assert.AreEqual(columnid, retrievedColumndef.columnid);

            // The grbit isn't asserted as esent will add some options by default
        }

        /// <summary>
        /// Add a column and retrieve its information using JetGetTableColumnInfo.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Add a column and retrieve its information using JetGetTableColumnInfo.")]
        public void JetGetTableColumnInfoByColumnid()
        {
            const string ColumnName = "column2";
            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF()
            {
                cbMax = 8192,
                cp = JET_CP.ASCII,
                coltyp = JET_coltyp.LongText,
                grbit = ColumndefGrbit.None,
            };

            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, ColumnName, columndef, null, 0, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            JET_COLUMNDEF retrievedColumndef;
            Api.JetGetTableColumnInfo(this.sesid, this.tableid, columnid, out retrievedColumndef);

            Assert.AreEqual(columndef.cbMax, retrievedColumndef.cbMax);
            Assert.AreEqual(columndef.cp, retrievedColumndef.cp);
            Assert.AreEqual(columndef.coltyp, retrievedColumndef.coltyp);
            Assert.AreEqual(columnid, retrievedColumndef.columnid);

            // The grbit isn't asserted as esent will add some options by default
        }

        /// <summary>
        /// Add a column and retrieve its information using JetGetColumnInfo.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Add a column and retrieve its information using JetGetColumnInfo.")]
        public void JetGetColumnInfo()
        {
            const string ColumnName = "column3";
            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF()
            {
                cbMax = 200,
                cp = JET_CP.ASCII,
                coltyp = JET_coltyp.LongText,
                grbit = ColumndefGrbit.None,
            };

            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, ColumnName, columndef, null, 0, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);
            JET_COLUMNDEF retrievedColumndef;
            Api.JetGetColumnInfo(this.sesid, this.dbid, this.table, ColumnName, out retrievedColumndef);

            Assert.AreEqual(columndef.cbMax, retrievedColumndef.cbMax);
            Assert.AreEqual(columndef.cp, retrievedColumndef.cp);
            Assert.AreEqual(columndef.coltyp, retrievedColumndef.coltyp);
            Assert.AreEqual(columnid, retrievedColumndef.columnid);

            // The grbit isn't asserted as esent will add some options by default
        }

        /// <summary>
        /// Add a column and retrieve its information using GetColumnDictionary.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Add a column and retrieve its information using GetColumnDictionary.")]
        public void GetColumnDictionary()
        {
            const string ColumnName = "column4";
            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF()
            {
                cbMax = 10000,
                cp = JET_CP.Unicode,
                coltyp = JET_coltyp.LongText,
                grbit = ColumndefGrbit.None,
            };

            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, ColumnName, columndef, null, 0, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            IDictionary<string, JET_COLUMNID> dict = Api.GetColumnDictionary(this.sesid, this.tableid);
            Assert.AreEqual(columnid, dict[ColumnName]);
        }

        /// <summary>
        /// Check that the dictionary returned by GetColumnDictionary is case-insensitive.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Check that the dictionary returned by GetColumnDictionary is case-insensitive.")]
        public void VerifyGetColumnDictionaryReturnsCaseInsensitiveDictionary()
        {
            IDictionary<string, JET_COLUMNID> dict = Api.GetColumnDictionary(this.sesid, this.tableid);
            Assert.AreEqual(this.columnidLongText, dict["tEsTcOLuMn"]);
        }

        /// <summary>
        /// Create an index.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Create an index.")]
        public void JetCreateIndex()
        {
            const string IndexDescription = "+TestColumn\0";
            const string IndexName = "new_index";
            Api.JetBeginTransaction(this.sesid);
            Api.JetCreateIndex(this.sesid, this.tableid, IndexName, CreateIndexGrbit.None, IndexDescription, IndexDescription.Length + 1, 100); 
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetSetCurrentIndex(this.sesid, this.tableid, IndexName);
        }

        /// <summary>
        /// Creates an index with JetCreateIndex2.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Creates an index with JetCreateIndex2.")]
        public void JetCreateIndex2()
        {
            Api.JetBeginTransaction(this.sesid);

            const string IndexName = "another_index";
            const string IndexDescription = "-TestColumn\0\0";
            var indexcreate = new JET_INDEXCREATE
            {
                szIndexName = IndexName,
                szKey = IndexDescription,
                cbKey = IndexDescription.Length,
                grbit = CreateIndexGrbit.IndexIgnoreAnyNull,
                ulDensity = 100,
            };
            Api.JetCreateIndex2(this.sesid, this.tableid, new[] { indexcreate }, 1);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetSetCurrentIndex(this.sesid, this.tableid, IndexName);
        }

        /// <summary>
        /// Verify that JetGetCurrentIndex returns the name of the index.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Verify that JetGetCurrentIndex returns the name of the index.")]
        public void VerifyJetGetCurrentIndexReturnsIndexName()
        {
            const string IndexDescription = "+TestColumn\0";
            const string IndexName = "myindexname";
            Api.JetBeginTransaction(this.sesid);
            Api.JetCreateIndex(this.sesid, this.tableid, IndexName, CreateIndexGrbit.None, IndexDescription, IndexDescription.Length + 1, 100);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetSetCurrentIndex(this.sesid, this.tableid, IndexName);
            string actual;
            Api.JetGetCurrentIndex(this.sesid, this.tableid, out actual, SystemParameters.NameMost);
            Assert.AreEqual(IndexName, actual);
        }

        /// <summary>
        /// Delete an index and make sure we can't use it afterwards.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Delete an index and make sure we can't use it afterwards.")]
        public void JetDeleteIndex()
        {
            const string IndexDescription = "+TestColumn\0";
            const string IndexName = "index_to_delete";
            Api.JetBeginTransaction(this.sesid);
            Api.JetCreateIndex(this.sesid, this.tableid, IndexName, CreateIndexGrbit.None, IndexDescription, IndexDescription.Length + 1, 100);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetDeleteIndex(this.sesid, this.tableid, IndexName);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            try
            {
                Api.JetSetCurrentIndex(this.sesid, this.tableid, IndexName);
                Assert.Fail("Index is still visible");
            }
            catch (EsentErrorException)
            {
            }
        }

        /// <summary>
        /// Delete a column and make sure we can't see it afterwards.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Delete a column and make sure we can't see it afterwards.")]
        public void TestJetDeleteColumn()
        {
            const string ColumnName = "column_to_delete";
            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF() { coltyp = JET_coltyp.Long };
            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, ColumnName, columndef, null, 0, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetDeleteColumn(this.sesid, this.tableid, ColumnName);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            try
            {
                // TODO: deal with versions of this API that return info on the deleted column
                Api.JetGetTableColumnInfo(this.sesid, this.tableid, ColumnName, out columndef);
                Assert.Fail("Column is still visible");
            }
            catch (EsentErrorException)
            {
            }
        }

        /// <summary>
        /// Delete a column with JetDeleteColumn2 and make sure we can't see it afterwards.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Delete a column with JetDeleteColumn2 and make sure we can't see it afterwards.")]
        public void TestJetDeleteColumn2()
        {
            const string ColumnName = "column_to_delete";
            Api.JetBeginTransaction(this.sesid);
            var columndef = new JET_COLUMNDEF() { coltyp = JET_coltyp.Long };
            JET_COLUMNID columnid;
            Api.JetAddColumn(this.sesid, this.tableid, ColumnName, columndef, null, 0, out columnid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetDeleteColumn2(this.sesid, this.tableid, ColumnName, DeleteColumnGrbit.None);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            try
            {
                // TODO: deal with versions of this API that return info on the deleted column
                Api.JetGetTableColumnInfo(this.sesid, this.tableid, ColumnName, out columndef);
                Assert.Fail("Column is still visible");
            }
            catch (EsentErrorException)
            {
            }
        }

        /// <summary>
        /// Delete a table and make sure we can't see it afterwards.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Delete a table and make sure we can't see it afterwards.")]
        public void DeleteTable()
        {
            const string TableName = "table_to_delete";
            Api.JetBeginTransaction(this.sesid);
            JET_TABLEID newtable;
            Api.JetCreateTable(this.sesid, this.dbid, TableName, 16, 100, out newtable);
            Api.JetCloseTable(this.sesid, newtable);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetDeleteTable(this.sesid, this.dbid, TableName);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            try
            {
                Api.JetOpenTable(this.sesid, this.dbid, TableName, null, 0, OpenTableGrbit.None, out newtable);
                Assert.Fail("Column is still visible");
            }
            catch (EsentErrorException)
            {
            }
        }

        #endregion DDL Tests

        #region DML Tests

        /// <summary>
        /// Insert a record and retrieve it.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and retrieve it.")]
        public void InsertRecord()
        {
            string s = Any.String;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(s);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);
            Assert.AreEqual(s, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Insert a record and update it.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and update it.")]
        public void ReplaceRecord()
        {
            string before = Any.String;
            string after = Any.String;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(before);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Replace);
            this.SetColumnFromString(after);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Assert.AreEqual(after, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Insert a record and update it. This uses the Transaction class.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Inserts a record and update it. This uses the Transaction class.")]
        public void ReplaceRecordWithTransactionClass()
        {
            string before = Any.String;
            string after = Any.String;

            using (var transaction = new Transaction(this.sesid))
            {
                Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
                this.SetColumnFromString(before);
                this.UpdateAndGotoBookmark();
                transaction.Commit(CommitTransactionGrbit.LazyFlush);
            }

            using (var transaction = new Transaction(this.sesid))
            {
                Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Replace);
                this.SetColumnFromString(after);
                this.UpdateAndGotoBookmark();
                transaction.Commit(CommitTransactionGrbit.LazyFlush);
            }

            Assert.AreEqual(after, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Insert a record, update it and rollback the update.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Inserts a record, update it and rollback the update.")]
        public void ReplaceAndRollback()
        {
            string before = Any.String;
            string after = Any.String;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(before);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Replace);
            this.SetColumnFromString(after);
            this.UpdateAndGotoBookmark();
            Api.JetRollback(this.sesid, RollbackTransactionGrbit.None);

            Assert.AreEqual(before, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Using JetBeginTransaction2, insert a record, update it and rollback the update.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Using JetBeginTransaction2, insert a record, update it and rollback the update.")]
        public void TestJetBeginTransaction2()
        {
            string before = Any.String;
            string after = Any.String;

            Api.JetBeginTransaction2(this.sesid, BeginTransactionGrbit.None);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(before);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Replace);
            this.SetColumnFromString(after);
            this.UpdateAndGotoBookmark();
            Api.JetRollback(this.sesid, RollbackTransactionGrbit.None);

            Assert.AreEqual(before, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Insert a record, update it and rollback the transaction.
        /// This uses the Transaction class.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record, update it and rollback the transaction. This uses the Transaction class.")]
        public void ReplaceAndRollbackWithTransactionClass()
        {
            string before = Any.String;
            string after = Any.String;

            using (var transaction = new Transaction(this.sesid))
            {
                Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
                this.SetColumnFromString(before);
                this.UpdateAndGotoBookmark();
                transaction.Commit(CommitTransactionGrbit.LazyFlush);
            }

            using (var transaction = new Transaction(this.sesid))
            {
                Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Replace);
                this.SetColumnFromString(after);
                this.UpdateAndGotoBookmark();

                // the transaction isn't committed
            }

            Assert.AreEqual(before, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Insert a record and delete it.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and delete it.")]
        public void InsertRecordAndDelete()
        {
            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetDelete(this.sesid, this.tableid);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            try
            {
                string x = this.RetrieveColumnAsString();
                Assert.Fail("Expected an EsentErrorException");
            }
            catch (EsentErrorException ex)
            {
                Assert.AreEqual(JET_err.RecordDeleted, ex.Error);
            }
        }

        /// <summary>
        /// Test JetGetLock.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Test JetGetLock.")]
        public void JetGetLock()
        {
            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            Api.JetBeginTransaction(this.sesid);
            Api.JetGetLock(this.sesid, this.tableid, GetLockGrbit.Read);
            Api.JetGetLock(this.sesid, this.tableid, GetLockGrbit.Write);
            Api.JetRollback(this.sesid, RollbackTransactionGrbit.None);
        }

        /// <summary>
        /// Verify that JetGetLock throws an exception when incompatible
        /// locks are requested.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Verify that JetGetLock throws an exception when incompatible locks are requested.")]
        public void VerifyJetGetLockThrowsExceptionOnWriteConflict()
        {
            var bookmark = new byte[SystemParameters.BookmarkMost];
            int bookmarkSize;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            Api.JetUpdate(this.sesid, this.tableid, bookmark, bookmark.Length, out bookmarkSize);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            JET_SESID otherSesid;
            JET_DBID otherDbid;
            JET_TABLEID otherTableid;
            Api.JetDupSession(this.sesid, out otherSesid);
            Api.JetOpenDatabase(otherSesid, this.database, null, out otherDbid, OpenDatabaseGrbit.None);
            Api.JetOpenTable(otherSesid, otherDbid, this.table, null, 0, OpenTableGrbit.None, out otherTableid);

            Api.JetGotoBookmark(this.sesid, this.tableid, bookmark, bookmarkSize);
            Api.JetGotoBookmark(otherSesid, otherTableid, bookmark, bookmarkSize);

            Api.JetBeginTransaction(this.sesid);
            Api.JetBeginTransaction(otherSesid);

            Api.JetGetLock(this.sesid, this.tableid, GetLockGrbit.Read);
            try
            {
                Api.JetGetLock(otherSesid, otherTableid, GetLockGrbit.Write);
                Assert.Fail("Expected an EsentErrorException");
            }
            catch (EsentErrorException ex)
            {
                Assert.AreEqual(JET_err.WriteConflict, ex.Error);
            }

            Api.JetRollback(this.sesid, RollbackTransactionGrbit.None);
            Api.JetRollback(otherSesid, RollbackTransactionGrbit.None);

            Api.JetCloseTable(otherSesid, otherTableid);
            Api.JetCloseDatabase(otherSesid, otherDbid, CloseDatabaseGrbit.None);
            Api.JetEndSession(otherSesid, EndSessionGrbit.None);
        }

        /// <summary>
        /// Call JetComputeStats.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Call JetComputeStats.")]
        public void JetComputeStats()
        {
            Api.JetBeginTransaction(this.sesid);
            for (int i = 0; i < 10; ++i)
            {
                Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
                this.SetColumnFromString(Any.String);
                this.UpdateAndGotoBookmark();
            }

            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);
            Api.JetComputeStats(this.sesid, this.tableid);
        }

        /// <summary>
        /// Call JetGetThreadStats.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Call JetGetThreadStats.")]
        public void JetGetThreadStats()
        {
            if (!EsentVersion.SupportsVistaFeatures)
            {
                return;
            }

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(Any.String);
            this.UpdateAndGotoBookmark();
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            JET_THREADSTATS threadstats;
            VistaApi.JetGetThreadStats(out threadstats);
            Assert.AreNotEqual(0, threadstats.cPageReferenced);
            Assert.AreNotEqual(0, threadstats.cLogRecord);
            Assert.AreNotEqual(0, threadstats.cbLogRecord);
        }

        #endregion DML Tests

        #region Navigation Tests

        /// <summary>
        /// Insert a record and retrieve its bookmark.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and retrieve its bookmark.")]
        public void JetGetBookmark()
        {
            var expectedBookmark = new byte[256];
            int expectedBookmarkSize;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            Api.JetUpdate(this.sesid, this.tableid, expectedBookmark, expectedBookmark.Length, out expectedBookmarkSize);
            Api.JetGotoBookmark(this.sesid, this.tableid, expectedBookmark, expectedBookmarkSize);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            var actualBookmark = new byte[256];
            int actualBookmarkSize;
            Api.JetGetBookmark(this.sesid, this.tableid, actualBookmark, actualBookmark.Length, out actualBookmarkSize);

            Assert.AreEqual(expectedBookmarkSize, actualBookmarkSize);
            for (int i = 0; i < expectedBookmarkSize; ++i)
            {
                Assert.AreEqual(expectedBookmark[i], actualBookmark[i]);
            }
        }

        /// <summary>
        /// Insert a record and retrieve its bookmark.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and retrieve its bookmark.")]
        public void GetBookmark()
        {
            var expectedBookmark = new byte[256];
            int expectedBookmarkSize;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            Api.JetUpdate(this.sesid, this.tableid, expectedBookmark, expectedBookmark.Length, out expectedBookmarkSize);
            Api.JetGotoBookmark(this.sesid, this.tableid, expectedBookmark, expectedBookmarkSize);
            Api.JetCommitTransaction(this.sesid, CommitTransactionGrbit.LazyFlush);

            byte[] actualBookmark = Api.GetBookmark(this.sesid, this.tableid);

            Assert.AreEqual(expectedBookmarkSize, actualBookmark.Length);
            for (int i = 0; i < expectedBookmarkSize; ++i)
            {
                Assert.AreEqual(expectedBookmark[i], actualBookmark[i]);
            }
        }

        /// <summary>
        /// Insert a record and retrieve its key.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and retrieve its key.")]
        public void JetRetrieveKey()
        {
            string expected = Any.String;
            var key = new byte[8192];

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(expected);
            this.UpdateAndGotoBookmark();

            int keyLength;
            Api.JetRetrieveKey(this.sesid, this.tableid, key, key.Length, out keyLength, RetrieveKeyGrbit.None);

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.UpdateAndGotoBookmark();

            Api.JetMakeKey(this.sesid, this.tableid, key, keyLength, MakeKeyGrbit.NormalizedKey);
            Api.JetSeek(this.sesid, this.tableid, SeekGrbit.SeekEQ);
            Assert.AreEqual(expected, this.RetrieveColumnAsString());
        }

        /// <summary>
        /// Insert a record and retrieve its key.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Insert a record and retrieve its key.")]
        public void RetrieveKey()
        {
            string expected = Any.String;

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.SetColumnFromString(expected);
            this.UpdateAndGotoBookmark();

            byte[] key = Api.RetrieveKey(this.sesid, this.tableid, RetrieveKeyGrbit.None);

            Api.JetBeginTransaction(this.sesid);
            Api.JetPrepareUpdate(this.sesid, this.tableid, JET_prep.Insert);
            this.UpdateAndGotoBookmark();

            Api.JetMakeKey(this.sesid, this.tableid, key, key.Length, MakeKeyGrbit.NormalizedKey);
            Api.JetSeek(this.sesid, this.tableid, SeekGrbit.SeekEQ);
            Assert.AreEqual(expected, this.RetrieveColumnAsString());
        }

        #endregion Navigation Tests

        #region JetDefragment Tests

        /// <summary>
        /// Test starting and stoppping OLD with JetDefragment.
        /// </summary>
        [TestMethod]
        [Priority(2)]
        [Description("Start and stop online defragmentation JetDefragment")]
        public void TestStartAndStopJetDefragment()
        {
            int passes = 1;
            int seconds = 1;
            Api.JetDefragment(this.sesid, this.dbid, null, ref passes, ref seconds, DefragGrbit.BatchStart);
            Api.JetDefragment(this.sesid, this.dbid, null, ref passes, ref seconds, DefragGrbit.BatchStop);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Update the cursor and goto the returned bookmark.
        /// </summary>
        private void UpdateAndGotoBookmark()
        {
            var bookmark = new byte[SystemParameters.BookmarkMost];
            int bookmarkSize;
            Api.JetUpdate(this.sesid, this.tableid, bookmark, bookmark.Length, out bookmarkSize);
            Api.JetGotoBookmark(this.sesid, this.tableid, bookmark, bookmarkSize);
        }

        /// <summary>
        /// Sets the LongText column in the table from a string. An update must be prepared.
        /// </summary>
        /// <param name="s">The string to set.</param>
        private void SetColumnFromString(string s)
        {
            byte[] data = Encoding.Unicode.GetBytes(s);
            Api.JetSetColumn(this.sesid, this.tableid, this.columnidLongText, data, data.Length, SetColumnGrbit.None, null);
        }

        /// <summary>
        /// Returns the value in the LongText column as a string. The cursor must be on a record.
        /// </summary>
        /// <returns>The value of the LongText column as a string.</returns>
        private string RetrieveColumnAsString()
        {
            return Api.RetrieveColumnAsString(this.sesid, this.tableid, this.columnidLongText, Encoding.Unicode);
        }

        #endregion HelperMethods
    }
}
