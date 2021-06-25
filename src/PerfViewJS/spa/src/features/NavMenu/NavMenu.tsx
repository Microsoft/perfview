import { ScrollablePane } from "@fluentui/react";
import { INavLink, INavLinkGroup, INavStyles, Nav } from "@fluentui/react/lib/Nav";
import React, { useEffect, useState } from "react";
import { RouteComponentProps, useHistory, withRouter } from "react-router-dom";

import { useDataFileContext } from "../../context/DataFileContext";

interface MatchParams {
  dataFile?: string;
}
export interface INavMenuProps extends RouteComponentProps<MatchParams> {}

const navStyles: Partial<INavStyles> = {
  root: {
    height: "100vh",
    boxSizing: "border-box",
    border: "1px solid #eee",
    overflowY: "auto",
  },
};

const navLinkGroups: INavLinkGroup[] = [
  {
    name: "PerfViewJS ",
    links: [],
  },
];

const NavMenu: React.FC = () => {
  const history = useHistory<MatchParams>();
  const [menuState, setMenuState] = useState<INavLinkGroup[]>(navLinkGroups);
  const { dataFile } = useDataFileContext();

  const onLinkClick = (ev?: React.MouseEvent<HTMLElement, MouseEvent> | undefined, item?: INavLink | undefined) => {
    ev?.preventDefault();
    if (!item) return;
    if (item.url === "/") history.push(item.url);
    else {
      history.push(item.url + dataFile);
    }
  };

  //* in case the datafile is default, we just bring user back to landing page to choose a trace file
  useEffect(() => {
    if (dataFile === "") history.push("/");
  }, [history]);

  useEffect(() => {
    const navLinks: INavLink[] = [
      {
        name: "Load file",
        key: "Load File",
        url: "/",
        onClick: onLinkClick,
        icon: "OpenFile",
      },
    ];
    //! workaround for dynamically setting fluent-ui navLinks
    if (dataFile) {
      navLinks.push(
        {
          name: "Trace Info",
          key: "Trace Info",
          url: `/ui/traceinfo/`,
          onClick: onLinkClick,
          icon: "Trackers",
        },
        {
          name: "Event Viewer",
          key: "Event Viewer",
          url: `/ui/eventviewer/`,
          onClick: onLinkClick,
          icon: "WorkItemBug",
        },
        {
          name: "Stack Viewer",
          key: "Stack Viewer",
          url: `/ui/stackviewer/eventlist/`,
          onClick: onLinkClick,
          icon: "Stack",
        },
        {
          name: "Process List",
          key: "Process List",
          url: `/ui/processlist/`,
          onClick: onLinkClick,
          icon: "ServerProcesses",
        },
        {
          name: "Module List",
          key: "Module List",
          url: `/ui/modulelist/`,
          onClick: onLinkClick,
          icon: "BacklogList",
        }
      );
    }
    menuState[0].links = navLinks;
    setMenuState([...menuState]);
  }, [dataFile]);
  return (
    <ScrollablePane scrollbarVisibility="auto">
      <Nav styles={navStyles} groups={navLinkGroups} />
    </ScrollablePane>
  );
};

const withRouterNavMenu = withRouter(NavMenu);
export { withRouterNavMenu as NavMenu };
