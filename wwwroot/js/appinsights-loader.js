// Application Insights JavaScript SDK Loader
// This file is loaded dynamically only when APPLICATIONINSIGHTS_CONNECTION_STRING is configured
// Connection string is injected via window.appInsightsConnectionString

(function() {
    // Check if connection string is available
    if (!window.appInsightsConnectionString) {
        console.log('[Application Insights] Client-side tracking disabled (no connection string)');
        return;
    }

    // Application Insights JavaScript SDK v3 Snippet
    !(function (cfg){function e(){cfg.onInit&&cfg.onInit(n)}var S,D,t,z,C,n,E=window,A=document,w=E.location,I="script",b="ingestionendpoint",xt="disableExceptionTracking",q="ai.device.";"instrumentationKey"[S="toLowerCase"](),D="crossOrigin",t="POST",z="appInsightsSDK",C=cfg.name||"appInsights",(cfg.name||E[z])&&(E[z]=C),n=E[C]||function(l){var u=!1,f=!1,m={initialize:!0,queue:[],sv:"8",version:2,config:l};function g(e,t){var n={},a="Browser";function i(e){e=""+e;return 1===e.length?"0"+e:e}return n[q+"id"]=a[S](),n[q+"type"]=a,n["ai.operation.name"]=w&&w.pathname||"_unknown_",n["ai.internal.sdkVersion"]="javascript:snippet_"+(m.sv||m.version),{time:(a=new Date).getUTCFullYear()+"-"+i(1+a.getUTCMonth())+"-"+i(a.getUTCDate())+"T"+i(a.getUTCHours())+":"+i(a.getUTCMinutes())+":"+i(a.getUTCSeconds())+"."+(a.getUTCMilliseconds()/1e3).toFixed(3).slice(2,5)+"Z",iKey:e,name:"Microsoft.ApplicationInsights."+e.replace(/-/g,"")+"."+t,sampleRate:100,tags:n,data:{baseData:{ver:2}},ver:void 0,seq:"1",aiDataContract:void 0}}var n,a,i,e,c=-1,s=0,o=["js.monitor.azure.com","js.cdn.applicationinsights.io","js.cdn.monitor.azure.com","js0.cdn.applicationinsights.io","js0.cdn.monitor.azure.com","js2.cdn.applicationinsights.io","js2.cdn.monitor.azure.com","az416426.vo.msecnd.net"],r=l.url||cfg.src,v=function(){return p(r,null)};function p(h,d){if((n=navigator)&&(~(n=(n.userAgent||"").toLowerCase()).indexOf("msie")||~n.indexOf("trident/"))&&~h.indexOf("ai.3")&&(h=h.replace(/(\/)(ai\.3\.)([^\d]*)$/,function(e,t,n){return t+"ai.2"+n})),!1!==cfg.cr)for(var t=0;t<o.length;t++)if(0<h.indexOf(o[t])){c=t;break}var n,a=function(e){var a,t,n,i,r,o,s,c,l,p;m.queue=[],f||(0<=c&&s+1<o.length?(a=(c+s+1)%o.length,y(h.replace(/^(.*\/\/)([\\w\.]*)(\\/.*)$/,function(e,t,n,i){return t+o[a]+i})),s+=1):(u=f=!0,s=h,!0!==cfg.dle&&(c=(t=function(){var e,t={},n=l.connectionString;if(n)for(var i=n.split(";"),a=0;a<i.length;a++){var r=i[a].split("=");2===r.length&&(t[r[0][S]()]=r[1])}return t[b]||(e=(n=t.endpointsuffix)?t.location:null,t[b]="https://"+(e?e+".":"")+"dc."+(n||"services.visualstudio.com")),t}()).instrumentationkey||l.instrumentationKey||"",t=(t=(t=t[b])&&"/"===t.slice(-1)?t.slice(0,-1):t)?t+"/v2/track":l.endpointUrl,t=l.userOverrideEndpointUrl||t,(n=[]).push((i="SDK LOAD Failure: Failed to load Application Insights SDK script (See stack for details)",r=s,l=t,(p=(o=g(c,"Exception")).data).baseType="ExceptionData",p.baseData.exceptions=[{typeName:"SDKLoadFailed",message:i.replace(/\\./g,"-"),hasFullStack:!1,stack:i+"\\nSnippet failed to load ["+r+"] -- Telemetry is disabled\\nHelp Link: https://go.microsoft.com/fwlink/?linkid=2128109\\nHost: "+(w&&w.pathname||"_unknown_")+"\\nEndpoint: "+l,parsedStack:[]}],o)),n.push((p=s,i=t,(l=(r=g(c,"Message")).data).baseType="MessageData",(o=l.baseData).message='AI (Internal): 99 message:"\\"'+("SDK LOAD Failure: Failed to load Application Insights SDK script (See stack for details) ("+p+")").replace(/\\"/g,"")+'\\"\"',o.properties={endpoint:i},r)),s=n,c=t,JSON&&((l=E.fetch)&&!cfg.useXhr?l(c,{method:t,body:JSON.stringify(s),mode:"cors"}):XMLHttpRequest&&((p=new XMLHttpRequest).open(t,c),p.setRequestHeader("Content-type","application/json"),p.send(JSON.stringify(s)))))))},i=function(e,t){f||setTimeout(function(){!t&&m.core||a()},500),u=!1},y=function(e){var t=A.createElement(I),e=(t.src=e,d&&(t.integrity=d),t.setAttribute("data-ai-name",C),cfg[D]);return!e&&""!==e||"undefined"==t[D]||(t[D]=e),t.onload=i,t.onerror=a,t.onreadystatechange=function(e,t){"loaded"!==t.readyState&&"complete"!==t.readyState||i(0,t)},cfg.ld&&cfg.ld<0?A.getElementsByTagName("head")[0].appendChild(t):setTimeout(function(){A.getElementsByTagName(I)[0].parentNode.appendChild(t)},cfg.ld||0),t};y(h)}cfg.sri&&(n=r.match(/^((http[s]?:\\\/\\\/.*)\\\/\\w+(\\.\\d+){1,5})\\.(([\\w]+\\.){0,2}js)$/))&&6===n.length?(h=n[1]+".integrity.json",a="@"+n[4],i=window.fetch,e=function(e){if(!e.ext||!e.ext[a]||!e.ext[a].file)throw Error("Error Loading JSON response");var t=e.ext[a].integrity||null;p(r=n[2]+e.ext[a].file,t)},i&&!cfg.useXhr?i(h,{method:"GET",mode:"cors"}).then(function(e){return e.json().catch(function(){return{}})}).then(e).catch(v):XMLHttpRequest&&((e=new XMLHttpRequest).open("GET",h),e.onreadystatechange=function(){if(e.readyState===XMLHttpRequest.DONE)if(200===e.status)try{e(JSON.parse(e.responseText))}catch(e){v()}else v()},e.send())):r&&v();try{m.cookie=A.cookie}catch(k){}function h(e){for(;e.length;)!function(t){m[t]=function(){var e=arguments;u||m.queue.push(function(){m[t].apply(m,e)})}}(e.pop())}var d,x,T="track",P="TrackPage",y="TrackEvent",T=(h([T+"Event",T+"PageView",T+"Exception",T+"Trace",T+"DependencyData",T+"Metric",T+"PageViewPerformance","start"+P,"stop"+P,"start"+y,"stop"+y,"addTelemetryInitializer","setAuthenticatedUserContext","clearAuthenticatedUserContext","flush"]),m.SeverityLevel={Verbose:0,Information:1,Warning:2,Error:3,Critical:4},(l.extensionConfig||{}).ApplicationInsightsAnalytics||{});return!0!==l[xt]&&!0!==T[xt]&&(h(["_"+(d="onerror")]),x=E[d],E[d]=function(e,t,n,a,i){var r=x&&x(e,t,n,a,i);return!0!==r&&m["_"+d]({message:e,url:t,lineNumber:n,columnNumber:a,error:i,evt:E.event}),r},l.autoExceptionInstrumented=!0),m}(cfg.cfg),(E[C]=n).queue&&0===n.queue.length?(n.queue.push(e),n.trackPageView({})):e();})(
        {
            "src": "https://js.monitor.azure.com/scripts/b/ai.3.gbl.min.js",
            "crossOrigin": "anonymous",
            "cfg": {
                "connectionString": window.appInsightsConnectionString,
                "enableAutoRouteTracking": true,
                "enableCorsCorrelation": true,
                "enableRequestHeaderTracking": true,
                "enableResponseHeaderTracking": true,
                "disableExceptionTracking": false,
                "disableFetchTracking": false,
                "disableAjaxTracking": false,
                "autoTrackPageVisitTime": true,
                "enableUnhandledPromiseRejectionTracking": true
            }
        }
    );

    console.log('[Application Insights] Client-side tracking enabled');
})();
