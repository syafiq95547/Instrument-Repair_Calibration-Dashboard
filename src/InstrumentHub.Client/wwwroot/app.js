window.instrumentHub={
 download:(name,type,base64)=>{const bytes=Uint8Array.from(atob(base64),c=>c.charCodeAt(0));const url=URL.createObjectURL(new Blob([bytes],{type}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);},
 focus:(selector)=>document.querySelector(selector)?.focus(),
 trapDialog:()=>{const dialog=document.querySelector('[role="dialog"]');if(!dialog)return;window.instrumentHub.previousFocus=document.activeElement;const selector='button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea, a[href]';dialog.querySelector(selector)?.focus();dialog.onkeydown=e=>{if(e.key!=='Tab')return;const items=[...dialog.querySelectorAll(selector)].filter(x=>x.offsetParent!==null);const first=items[0],last=items[items.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}};},
 restoreFocus:()=>window.instrumentHub.previousFocus?.focus()
};
