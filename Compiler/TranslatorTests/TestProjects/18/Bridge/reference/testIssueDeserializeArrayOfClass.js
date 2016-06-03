﻿(function (globals) {
    "use strict";

    Bridge.define('TestIssueDeserializeArrayOfClass.Foo', {
        someMethod: function () {
        }
    });
    
    Bridge.define('TestIssueDeserializeArrayOfClass.IssueDeserializeArrayOfClass', {
        statics: {
            config: {
                init: function () {
                    Bridge.ready(this.main);
                }
            },
            main: function () {
                var ser = JSON.stringify([new TestIssueDeserializeArrayOfClass.Foo()]);
                var deser = Bridge.merge(new Array(), JSON.parse(ser), function(){return new TestIssueDeserializeArrayOfClass.Foo();});
    
                try {
                    deser[0].someMethod();
                    System.Console.log("ok[array]");
                }
                catch (ex) {
                    ex = System.Exception.create(ex);
                    System.Console.log("BUG[array]: " + ex);
                }
            }
        },
        $entryPoint: true
    });
    
    
    
    Bridge.init();
})(this);