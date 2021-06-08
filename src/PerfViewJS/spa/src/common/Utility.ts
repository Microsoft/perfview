import base64url from "base64url";

export const transformStringArrayToDetailListItems = (items: string[]) =>
  items.map((item, i) => ({
    key: i,
    name: item,
    value: item,
  }));

export const constructAPICacheKeyFromRouteKey = (route: string) => {
  const routeKey = JSON.parse(base64url.decode(route));
  return (
    "filename=" +
    routeKey.a +
    "&stackType=" +
    routeKey.b +
    "&pid=" +
    routeKey.c +
    "&start=" +
    base64url.encode(routeKey.d) +
    "&end=" +
    base64url.encode(routeKey.e) +
    "&groupPats=" +
    base64url.encode(routeKey.f) +
    "&foldPats=" +
    base64url.encode(routeKey.g) +
    "&incPats=" +
    base64url.encode(routeKey.h) +
    "&excPats=" +
    base64url.encode(routeKey.i) +
    "&foldPct=" +
    routeKey.j +
    "&drillIntoKey=" +
    routeKey.k
  );
};
