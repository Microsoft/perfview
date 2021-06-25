import Enzyme from "enzyme";
import Adapter from "@wojtekmaj/enzyme-adapter-react-17";
import { setIconOptions } from "@fluentui/react/lib/Styling";

Enzyme.configure({ adapter: new Adapter() });

// https://github.com/microsoft/fluentui/wiki/Using-icons#test-scenarios
// Suppress icon warnings.
setIconOptions({
  disableWarnings: true,
});
