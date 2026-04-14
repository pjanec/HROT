using Xunit;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Tkb.Tests
{
    public class TkbDatabaseTests
    {
        [Fact]
        public void Register_StoresTemplateCorrectly()
        {
            var db = new TkbDatabase();
            var template = new TkbTemplate("Test", 123);
            
            db.Register(template);
            
            Assert.Same(template, db.GetByName("Test"));
            Assert.Same(template, db.GetByType(123));
        }

        [Fact]
        public void Register_ThrowsOnCollision()
        {
            var db = new TkbDatabase();
            var t1 = new TkbTemplate("Test", 1);
            var t2 = new TkbTemplate("Test", 2); // Same name
            var t3 = new TkbTemplate("Other", 1); // Same ID as t1
            
            db.Register(t1);
            
            Assert.Throws<InvalidOperationException>(() => db.Register(t2));
            Assert.Throws<InvalidOperationException>(() => db.Register(t3));
        }

        [Fact]
        public void GetByType_ThrowsWhenNotFound()
        {
            var db = new TkbDatabase();
            Assert.Throws<KeyNotFoundException>(() => db.GetByType(999));
        }
        
        [Fact]
        public void TryGetByType_ReturnsFalseWhenNotFound()
        {
            var db = new TkbDatabase();
            bool found = db.TryGetByType(999, out var t);
            Assert.False(found);
            Assert.Null(t);
        }
    }
}
