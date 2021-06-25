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

export const copyAndSort = <T>(items: T[], columnKey: string, isSortedDescending?: boolean): T[] => {
  const key = columnKey as keyof T;
  return items.slice(0).sort((a: T, b: T) => {
    let first, second;
    if (!isNaN(parseFloat(a[key] + ""))) {
      first = parseFloat(a[key] + "");
    } else if (!isNaN(parseInt(a + ""))) {
      first = parseInt(a[key] + "");
    } else {
      first = a[key];
    }
    if (!isNaN(parseFloat(b[key] + ""))) {
      second = parseFloat(b[key] + "");
    } else if (!isNaN(parseInt(b + ""))) {
      second = parseInt(b[key] + "");
    } else {
      second = b[key];
    }
    return (isSortedDescending ? first < second : first > second) ? 1 : -1;
  });
};
