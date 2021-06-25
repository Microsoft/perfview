import React from "react";
import { useLocalStorage } from "../hooks/useLocalStorage";

/**
 * magic letters lookup
 * routeKey
 a = filname
 b = stackType
 c = pid (Process Id)
 d = start (Relative Start Time (ms))
 e = end (Relative End Time (ms))
 f = groupPats (Grouping Patterns (Regex))
 g = foldPats (Folding Patterns (Regex))
 h = incPats (Include Patterns (Regex))
 i = excPats (Exclude Patterns (Regex))
 j = foldPct = MinInclusiveTimePercent (CallTreeData.cs)
 k = drillIntoKey = DrillIntoKey

'filename=' + routeKey.a +
'&stackType=' + routeKey.b +
'&pid=' + routeKey.c +
'&start=' + base64url.encode(routeKey.d, "utf8") +
'&end=' + base64url.encode(routeKey.e, "utf8") +
'&groupPats=' + base64url.encode(routeKey.f, "utf8") +
'&foldPats=' + base64url.encode(routeKey.g, "utf8") +
'&incPats=' + base64url.encode(routeKey.h, "utf8") +
'&excPats=' + base64url.encode(routeKey.i, "utf8") +
'&foldPct=' + routeKey.j +
'&drillIntoKey=' + routeKey.k;
 */

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore
const RouteKeyContext = React.createContext<RouteKeyContextType>();

export type RouteKeyContextType = {
  routeKey: string;
  setRouteKey: (routeKey: string) => void;
};

interface IRouteKeyContextProviderProp {
  children: JSX.Element;
}

export const RouteKeyContextProvider = (props: IRouteKeyContextProviderProp) => {
  const { children } = props;
  const [routeKey, _setRouteKey] = useLocalStorage<string>("routeKey", "");

  const setRouteKey = (_routeKey: string) => _setRouteKey(_routeKey);
  const value = { routeKey, setRouteKey };
  return <RouteKeyContext.Provider value={value}>{children}</RouteKeyContext.Provider>;
};

export const useRouteKeyContext = () => {
  const context = React.useContext(RouteKeyContext);
  if (context === undefined) throw new Error("useRouteKeyContext context must be used within RouteKeyContextProvider");
  return context;
};
